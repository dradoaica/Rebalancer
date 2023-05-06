namespace Rebalancer.SqlServer;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clients;
using Core;
using Core.Logging;
using Leases;
using Resources;
using Roles;
using Store;

public class SqlServerProvider : IRebalancerProvider
{
    private static readonly object startLockObj = new();
    private readonly IClientService clientService;
    private readonly string connectionString;
    private readonly Coordinator coordinator;
    private readonly Follower follower;
    private readonly ILeaseService leaseService;
    private readonly IRebalancerLogger logger;
    private readonly IResourceService resourceService;
    private readonly ResourceGroupStore store;
    private Guid clientId;
    private bool isCoordinator;
    private Task mainTask;
    private string resourceGroup;
    private bool started;

    public SqlServerProvider(string connectionString,
        IRebalancerLogger logger = null,
        ILeaseService leaseService = null,
        IResourceService resourceService = null,
        IClientService clientService = null)
    {
        this.connectionString = connectionString;
        this.store = new ResourceGroupStore();

        if (logger == null)
        {
            this.logger = new NullRebalancerLogger();
        }
        else
        {
            this.logger = logger;
        }

        if (leaseService == null)
        {
            this.leaseService = new LeaseService(this.connectionString, this.logger);
        }
        else
        {
            this.leaseService = leaseService;
        }

        if (resourceService == null)
        {
            this.resourceService = new ResourceService(this.connectionString);
        }
        else
        {
            this.resourceService = resourceService;
        }

        if (clientService == null)
        {
            this.clientService = new ClientService(this.connectionString);
        }
        else
        {
            this.clientService = clientService;
        }

        this.clientId = Guid.NewGuid();
        this.coordinator = new Coordinator(this.logger, this.resourceService, this.clientService, this.store);
        this.follower = new Follower(this.logger, this.clientService, this.store);
    }

    public async Task StartAsync(string resourceGroup,
        OnChangeActions onChangeActions,
        CancellationToken parentToken,
        ClientOptions clientOptions)
    {
        // just in case someone does some concurrency
        lock (startLockObj)
        {
            if (this.started)
            {
                throw new RebalancerException("Context already started");
            }

            this.started = true;
        }

        this.resourceGroup = resourceGroup;
        await this.clientService.CreateClientAsync(this.resourceGroup, this.clientId);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        this.mainTask = Task.Run(async () =>
        {
            while (!parentToken.IsCancellationRequested)
            {
                CancellationTokenSource childTaskCts = new();
                try
                {
                    BlockingCollection<ClientEvent> clientEvents = new();

                    var leaderElectionTask = this.StartLeadershipTask(childTaskCts.Token, clientEvents);
                    var roleTask = this.StartRoleTask(clientOptions.OnAssignmentDelay, childTaskCts.Token,
                        onChangeActions,
                        clientEvents);

                    while (!parentToken.IsCancellationRequested
                           && !leaderElectionTask.IsCompleted
                           && !roleTask.IsCompleted
                           && !clientEvents.IsCompleted)
                    {
                        await Task.Delay(100);
                    }

                    // cancel child tasks
                    childTaskCts.Cancel();

                    if (parentToken.IsCancellationRequested)
                    {
                        this.logger.Info(this.clientId.ToString(), "Context shutting down due to cancellation");
                    }
                    else
                    {
                        if (leaderElectionTask.IsFaulted)
                        {
                            await this.NotifyOfErrorAsync(leaderElectionTask,
                                $"Shutdown due to leader election task fault. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                onChangeActions);
                        }
                        else if (roleTask.IsFaulted)
                        {
                            await this.NotifyOfErrorAsync(roleTask,
                                $"Shutdown due to coordinator/follower task fault. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                onChangeActions);
                        }
                        else
                        {
                            this.NotifyOfError(onChangeActions,
                                $"Unknown shutdown reason. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                null);
                        }

                        if (clientOptions.AutoRecoveryOnError)
                        {
                            await this.WaitFor(clientOptions.RestartDelay, parentToken);
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (!leaderElectionTask.IsFaulted)
                    {
                        await leaderElectionTask;
                    }
                    else
                    {
                        // avoid UNOBSERVED TASK EXCEPTION by accessing its Exception property
                        var exMsg = leaderElectionTask.Exception?.GetBaseException().Message;
                    }

                    if (!roleTask.IsFaulted)
                    {
                        await roleTask;
                    }
                    else
                    {
                        // avoid UNOBSERVED TASK EXCEPTION by accessing its Exception property
                        var exMsg = roleTask.Exception?.GetBaseException().Message;
                    }

                    await this.clientService.SetClientStatusAsync(this.clientId, ClientStatus.Terminated);

                    if (this.isCoordinator)
                    {
                        await this.leaseService.RelinquishLeaseAsync(new RelinquishLeaseRequest
                        {
                            ClientId = this.clientId,
                            FencingToken = this.coordinator.GetCurrentFencingToken(),
                            ResourceGroup = this.resourceGroup
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.NotifyOfError(onChangeActions,
                        $"An unexpected error has caused shutdown. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                        ex);

                    if (clientOptions.AutoRecoveryOnError)
                    {
                        await this.WaitFor(clientOptions.RestartDelay, parentToken);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    public async Task RecreateClientAsync()
    {
        if (!this.started)
        {
            throw new RebalancerException("Context not started");
        }

        await this.clientService.CreateClientAsync(this.resourceGroup, this.clientId);
    }

    public async Task WaitForCompletionAsync()
    {
        try
        {
            await this.mainTask;
        }
        catch (Exception ex)
        {
            this.logger.Error(this.clientId.ToString(), ex);
        }
    }

    public AssignedResources GetAssignedResources()
    {
        while (true)
        {
            var response = this.store.GetResources();
            if (response.AssignmentStatus == AssignmentStatus.ResourcesAssigned ||
                response.AssignmentStatus == AssignmentStatus.NoResourcesAssigned)
            {
                return new AssignedResources
                {
                    Resources = response.Resources, ClientState = this.GetState(response.AssignmentStatus)
                };
            }

            Thread.Sleep(100);
        }
    }

    public ClientState GetState()
    {
        if (this.started)
        {
            var response = this.store.GetResources();
            return this.GetState(response.AssignmentStatus);
        }

        return ClientState.NotStarted;
    }

    private async Task NotifyOfErrorAsync(Task faultedTask, string message, OnChangeActions onChangeActions)
    {
        await this.InvokeOnErrorAsync(faultedTask, message, onChangeActions);
        this.InvokeOnStop(onChangeActions);
    }

    private void NotifyOfError(OnChangeActions onChangeActions, string message, Exception exception)
    {
        this.InvokeOnError(onChangeActions, message, exception);
        this.InvokeOnStop(onChangeActions);
    }

    private async Task InvokeOnErrorAsync(Task faultedTask, string message, OnChangeActions onChangeActions)
    {
        try
        {
            await faultedTask;
        }
        catch (Exception ex)
        {
            this.InvokeOnError(onChangeActions, message, ex);
        }
    }

    private void InvokeOnError(OnChangeActions onChangeActions, string message, Exception exception)
    {
        try
        {
            foreach (var onAbortActions in onChangeActions.OnAbortActions)
            {
                onAbortActions.Invoke(message, exception);
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(this.clientId.ToString(), ex.ToString());
        }
    }

    private void InvokeOnStop(OnChangeActions onChangeActions)
    {
        this.coordinator.SetStoppedDueToInternalErrorFlag();
        try
        {
            foreach (var onErrorAction in onChangeActions.OnStopActions)
            {
                onErrorAction.Invoke();
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(this.clientId.ToString(), ex.ToString());
        }
    }

    private Task StartLeadershipTask(CancellationToken token,
        BlockingCollection<ClientEvent> clientEvents) =>
        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    AcquireLeaseRequest request = new() {ClientId = this.clientId, ResourceGroup = this.resourceGroup};
                    var response = await this.TryAcquireLeaseAsync(request, token);
                    if (response.Result == LeaseResult.Granted) // is now the Coordinator
                    {
                        await this.ExecuteLeaseRenewals(token, clientEvents, response.Lease);
                    }
                    else if (response.Result == LeaseResult.Denied) // is a Follower
                    {
                        this.PostFollowerEvent(response.Lease.ExpiryPeriod, clientEvents);
                        await this.WaitFor(this.GetInterval(response.Lease.HeartbeatPeriod), token);
                    }
                    else if (response.Result == LeaseResult.NoLease)
                    {
                        throw new RebalancerException($"The resource group {this.resourceGroup} does not exist.");
                    }
                    else if (response.IsErrorResponse())
                    {
                        throw new RebalancerException("An non-recoverable error occurred.", response.Exception);
                    }
                    else
                    {
                        throw new RebalancerException(
                            "A non-supported lease result was received"); // should never happen, just in case I screw up in the future
                    }
                }
            }
            finally
            {
                clientEvents.CompleteAdding();
            }
        });

    private async Task ExecuteLeaseRenewals(CancellationToken token,
        BlockingCollection<ClientEvent> clientEvents,
        Lease lease)
    {
        CoordinatorToken coordinatorToken = new();
        this.PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
        await this.WaitFor(this.GetInterval(lease.HeartbeatPeriod), token, coordinatorToken);

        // lease renewal loop
        while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
        {
            var response = await this.TryRenewLeaseAsync(
                new RenewLeaseRequest
                {
                    ClientId = this.clientId, ResourceGroup = this.resourceGroup, FencingToken = lease.FencingToken
                }, token);
            if (response.Result == LeaseResult.Granted)
            {
                this.PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
                await this.WaitFor(this.GetInterval(lease.HeartbeatPeriod), token, coordinatorToken);
            }
            else if (response.Result == LeaseResult.Denied)
            {
                this.PostFollowerEvent(lease.ExpiryPeriod, clientEvents);
                await this.WaitFor(this.GetInterval(lease.HeartbeatPeriod), token);
                break;
            }
            else if (response.Result == LeaseResult.NoLease)
            {
                throw new RebalancerException($"The resource group {this.resourceGroup} does not exist.");
            }
            else if (response.IsErrorResponse())
            {
                throw new RebalancerException("An non-recoverable error occurred.", response.Exception);
            }
            else
            {
                throw
                    new RebalancerException(
                        "A non-supported lease result was received"); // should never happen, just in case I screw up in the future
            }
        }
    }

    private async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request, CancellationToken token)
    {
        var delaySeconds = 2;
        var triesLeft = 3;
        while (triesLeft > 0)
        {
            triesLeft--;
            var response = await this.leaseService.TryAcquireLeaseAsync(request);
            if (response.Result != LeaseResult.TransientError)
            {
                return response;
            }

            if (triesLeft > 0)
            {
                await this.WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
            }
            else
            {
                return response;
            }

            delaySeconds = delaySeconds * 2;
        }

        // this should never happen
        return new LeaseResponse {Result = LeaseResult.Error};
    }

    private async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request, CancellationToken token)
    {
        var delaySeconds = 2;
        var triesLeft = 3;
        while (triesLeft > 0)
        {
            triesLeft--;
            var response = await this.leaseService.TryRenewLeaseAsync(request);
            if (response.Result != LeaseResult.TransientError)
            {
                return response;
            }

            if (triesLeft > 0)
            {
                await this.WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
            }
            else
            {
                return response;
            }

            delaySeconds = delaySeconds * 2;
        }

        // this should never happen
        return new LeaseResponse {Result = LeaseResult.Error};
    }

    private TimeSpan GetInterval(TimeSpan leaseExpiry) =>
        TimeSpan.FromMilliseconds(leaseExpiry.TotalMilliseconds / 2.5);

    private void PostLeaderEvent(int fencingToken,
        TimeSpan keepAliveExpiryPeriod,
        CoordinatorToken coordinatorToken,
        BlockingCollection<ClientEvent> clientEvents)
    {
        this.logger.Debug(this.clientId.ToString(), $"{this.clientId} is leader");
        this.isCoordinator = true;
        ClientEvent clientEvent = new()
        {
            ResourceGroup = this.resourceGroup,
            EventType = EventType.Coordinator,
            FencingToken = fencingToken,
            CoordinatorToken = coordinatorToken,
            KeepAliveExpiryPeriod = keepAliveExpiryPeriod
        };
        clientEvents.Add(clientEvent);
    }

    private void PostFollowerEvent(TimeSpan keepAliveExpiryPeriod,
        BlockingCollection<ClientEvent> clientEvents)
    {
        this.logger.Debug(this.clientId.ToString(), $"{this.clientId} is follower");
        this.isCoordinator = false;
        ClientEvent clientEvent = new()
        {
            EventType = EventType.Follower,
            ResourceGroup = this.resourceGroup,
            KeepAliveExpiryPeriod = keepAliveExpiryPeriod
        };
        clientEvents.Add(clientEvent);
    }

    private Task StartRoleTask(TimeSpan onStartDelay,
        CancellationToken token,
        OnChangeActions onChangeActions,
        BlockingCollection<ClientEvent> clientEvents) =>
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !clientEvents.IsAddingCompleted)
            {
                // take the most recent event, if multiple are queued up then we only need the latest
                ClientEvent clientEvent = null;
                while (clientEvents.Any())
                {
                    try
                    {
                        clientEvent = clientEvents.Take(token);
                    }
                    catch (OperationCanceledException) { }
                }

                // if there was an event then call the appropriate role beahvaiour
                if (clientEvent != null)
                {
                    if (clientEvent.EventType == EventType.Coordinator)
                    {
                        if (onStartDelay.Ticks > 0)
                        {
                            this.logger.Debug(this.clientId.ToString(),
                                $"Coordinator - Delaying on start for {(int)onStartDelay.TotalMilliseconds}ms");
                            await this.WaitFor(onStartDelay, token);
                        }

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await this.coordinator.ExecuteCoordinatorRoleAsync(this.clientId,
                            clientEvent,
                            onChangeActions,
                            token);
                    }
                    else
                    {
                        if (onStartDelay.Ticks > 0)
                        {
                            this.logger.Debug(this.clientId.ToString(),
                                $"Follower - Delaying on start for {(int)onStartDelay.TotalMilliseconds}ms");
                            await this.WaitFor(onStartDelay, token);
                        }

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await this.follower.ExecuteFollowerRoleAsync(this.clientId,
                            clientEvent,
                            onChangeActions,
                            token);
                    }
                }
                else
                {
                    await this.WaitFor(TimeSpan.FromSeconds(1), token);
                }
            }
        });

    private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token, CoordinatorToken coordinatorToken)
    {
        Stopwatch sw = new();
        sw.Start();
        while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
        {
            if (sw.Elapsed < delayPeriod)
            {
                await Task.Delay(100);
            }
            else
            {
                break;
            }
        }
    }

    private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayPeriod, token);
        }
        catch (TaskCanceledException)
        {
        }
    }

    public AssignedResources GetAssignedResources(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var response = this.store.GetResources();
            if (response.AssignmentStatus == AssignmentStatus.ResourcesAssigned ||
                response.AssignmentStatus == AssignmentStatus.NoResourcesAssigned)
            {
                return new AssignedResources
                {
                    Resources = response.Resources, ClientState = this.GetState(response.AssignmentStatus)
                };
            }

            Thread.Sleep(100);
        }

        return new AssignedResources {Resources = new List<string>(), ClientState = ClientState.Stopped};
    }

    private ClientState GetState(AssignmentStatus assignmentState)
    {
        switch (assignmentState)
        {
            case AssignmentStatus.ResourcesAssigned:
            case AssignmentStatus.NoResourcesAssigned:
                return ClientState.Assigned;
            case AssignmentStatus.AssignmentInProgress:
            default:
                return ClientState.PendingAssignment;
        }
    }
}
