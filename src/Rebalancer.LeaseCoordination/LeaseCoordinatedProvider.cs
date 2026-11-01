using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.LeaseCoordination.Roles;
using Rebalancer.LeaseCoordination.Store;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination;

/// <summary>
///     The client loop of a lease-based backend (ADR-0029): leader election under a lease, the coordinator and
///     follower roles, recovery and clean stop. A backend provider builds its services and delegates to this class.
/// </summary>
internal sealed class LeaseCoordinatedProvider : IRebalancerProvider
{
    // guards this instance's started flag; one per provider, so providers in one process do not share it
    private readonly object startLockObj = new();

    // the rebalancer.backend tag of the telemetry (ADR-0022), e.g. "sqlserver" or "redis"
    private readonly string backend;
    private readonly Guid clientId;
    private readonly IClientService clientService;
    private readonly Coordinator coordinator;
    private readonly Follower follower;
    private readonly ILeaseService leaseService;
    private readonly IRebalancerLogger logger;

    // backend work after the start check and before the client record is created (resource group, client id)
    private readonly Func<string, string, Task> onStarting;
    private readonly ResourceGroupStore store;
    private volatile bool aborted;
    private ClientOptions currentClientOptions;
    private string currentResourceGroup;
    private volatile bool isCoordinator;

    // the token of the lease this node was granted, so a relinquish names it even if the role never ran
    private volatile int leaseFencingToken;
    private Task mainTask;
    private bool started;
    private volatile bool stopped;

    public LeaseCoordinatedProvider(
        string backend,
        IRebalancerLogger logger,
        ILeaseService leaseService,
        IResourceService resourceService,
        IClientService clientService,
        Func<string, string, Task> onStarting = null
    )
    {
        this.backend = backend;
        this.logger = logger;
        this.leaseService = leaseService;
        this.clientService = clientService;
        this.onStarting = onStarting;
        store = new ResourceGroupStore();
        clientId = Guid.NewGuid();
        coordinator = new Coordinator(backend, logger, resourceService, clientService, store);
        follower = new Follower(logger, clientService, store);
    }

    public async Task StartAsync(
        string resourceGroup,
        OnChangeActions onChangeActions,
        CancellationToken parentToken,
        ClientOptions clientOptions
    )
    {
        // just in case someone does some concurrency
        lock (startLockObj)
        {
            if (started)
            {
                throw new RebalancerException("Context already started");
            }

            started = true;
        }

        currentResourceGroup = resourceGroup;
        currentClientOptions = clientOptions;
        if (onStarting != null)
        {
            await onStarting(resourceGroup, clientId.ToString());
        }

        await clientService.CreateClientAsync(currentResourceGroup, clientId);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        mainTask = Task.Run(async () =>
            {
                while (!parentToken.IsCancellationRequested)
                {
                    CancellationTokenSource childTaskCts = new();
                    try
                    {
                        BlockingCollection<ClientEvent> clientEvents = new();

                        var leaderElectionTask = StartLeadershipTask(childTaskCts.Token, clientEvents);
                        var roleTask = StartRoleTask(childTaskCts.Token, onChangeActions, clientEvents);

                        while (!parentToken.IsCancellationRequested &&
                               !leaderElectionTask.IsCompleted &&
                               !roleTask.IsCompleted &&
                               !clientEvents.IsCompleted)
                        {
                            await WaitFor(TimeSpan.FromMilliseconds(100), parentToken);
                        }

                        // cancel child tasks
                        childTaskCts.Cancel();

                        if (parentToken.IsCancellationRequested)
                        {
                            logger.Info(clientId.ToString(), "Context shutting down due to cancellation");
                        }
                        else
                        {
                            if (leaderElectionTask.IsFaulted)
                            {
                                await NotifyOfErrorAsync(
                                    leaderElectionTask,
                                    $"Shutdown due to leader election task fault. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                    onChangeActions
                                );
                            }
                            else if (roleTask.IsFaulted)
                            {
                                await NotifyOfErrorAsync(
                                    roleTask,
                                    $"Shutdown due to coordinator/follower task fault. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                    onChangeActions
                                );
                            }
                            else
                            {
                                NotifyOfError(
                                    onChangeActions,
                                    $"Unknown shutdown reason. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                                    null
                                );
                            }

                            if (clientOptions.AutoRecoveryOnError)
                            {
                                await WaitFor(clientOptions.RestartDelay, parentToken);
                            }
                            else
                            {
                                aborted = true; // gave up after an error: GetState reports Aborted (US-0021)
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
                            _ = leaderElectionTask.Exception;
                        }

                        if (!roleTask.IsFaulted)
                        {
                            await roleTask;
                        }
                        else
                        {
                            // avoid UNOBSERVED TASK EXCEPTION by accessing its Exception property
                            _ = roleTask.Exception;
                        }

                        if (parentToken.IsCancellationRequested)
                        {
                            // clean stop: the handlers stop before the record goes, so the coordinator reassigns only
                            // resources nobody processes; this client id is never used again (ADR-0014)
                            InvokeOnStopForCleanStop(onChangeActions);
                            await clientService.DeleteClientAsync(clientId);
                        }
                        else if (clientService is IFencedClientService fenced)
                        {
                            // restart after an error (PRB-0005): the record is reused as a new registration would
                            // write it, so a coordinator includes this client again
                            await fenced.ResetClientAsync(clientId);
                        }
                        else
                        {
                            // restart after an error with a custom client service: the record is reused
                            // (Terminated -> Waiting) only if this client becomes the coordinator (PRB-0005)
                            await clientService.SetClientStatusAsync(clientId, ClientStatus.Terminated);
                        }

                        if (isCoordinator)
                        {
                            await leaseService.RelinquishLeaseAsync(
                                new RelinquishLeaseRequest
                                {
                                    ClientId = clientId,
                                    FencingToken = leaseFencingToken,
                                    ResourceGroup = currentResourceGroup,
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        NotifyOfError(
                            onChangeActions,
                            $"An unexpected error has caused shutdown. Automatic restart is set to {clientOptions.AutoRecoveryOnError}",
                            ex
                        );

                        if (clientOptions.AutoRecoveryOnError)
                        {
                            await WaitFor(clientOptions.RestartDelay, parentToken);
                        }
                        else
                        {
                            aborted = true; // gave up after an error: GetState reports Aborted (US-0021)
                            break;
                        }
                    }
                }

                // PRB-0006: the loop ended on cancellation (StopAsync), not because the client gave up
                if (parentToken.IsCancellationRequested && !aborted)
                {
                    stopped = true;
                }
            }
        );
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    public async Task RecreateClientAsync()
    {
        if (!started)
        {
            throw new RebalancerException("Context not started");
        }

        await clientService.CreateClientAsync(currentResourceGroup, clientId);
    }

    public async Task WaitForCompletionAsync()
    {
        try
        {
            await mainTask;
        }
        catch (Exception ex)
        {
            logger.Error(clientId.ToString(), ex);
        }
    }

    /// <summary>
    ///     Returns at once (ADR 0007): while assignment is pending the state is PendingAssignment and no resources are
    ///     returned. To wait for an assignment use <see cref="GetAssignedResources(CancellationToken)" />.
    /// </summary>
    public AssignedResources GetAssignedResources()
    {
        if (aborted || stopped)
        {
            return new AssignedResources
            {
                Resources = new List<string>(),
                ClientState = aborted ? ClientState.Aborted : ClientState.Stopped,
            };
        }

        var response = store.GetResources();
        return new AssignedResources
        {
            Resources = response.Resources,
            ClientState = GetState(response.AssignmentStatus),
        };
    }

    public ClientState GetState()
    {
        if (aborted)
        {
            return ClientState.Aborted;
        }

        if (stopped)
        {
            return ClientState.Stopped;
        }

        if (started)
        {
            var response = store.GetResources();
            return GetState(response.AssignmentStatus);
        }

        return ClientState.NotStarted;
    }

    private async Task NotifyOfErrorAsync(Task faultedTask, string message, OnChangeActions onChangeActions)
    {
        await InvokeOnErrorAsync(faultedTask, message, onChangeActions);
        InvokeOnStop(onChangeActions);
    }

    private void NotifyOfError(OnChangeActions onChangeActions, string message, Exception exception)
    {
        InvokeOnError(onChangeActions, message, exception);
        InvokeOnStop(onChangeActions);
    }

    private async Task InvokeOnErrorAsync(Task faultedTask, string message, OnChangeActions onChangeActions)
    {
        try
        {
            await faultedTask;
        }
        catch (Exception ex)
        {
            InvokeOnError(onChangeActions, message, ex);
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
            logger.Error(clientId.ToString(), ex.ToString());
        }
    }

    // StopAsync raises OnUnassignment, as documented and as ZooKeeper does; only if the handlers were started
    private void InvokeOnStopForCleanStop(OnChangeActions onChangeActions)
    {
        if (store.GetResources().AssignmentStatus == AssignmentStatus.AssignmentInProgress)
        {
            return;
        }

        try
        {
            foreach (var onStopAction in onChangeActions.OnStopActions)
            {
                onStopAction.Invoke();
            }
        }
        catch (Exception ex)
        {
            logger.Error(clientId.ToString(), ex.ToString());
        }

        store.SetResources(
            new SetResourcesRequest
            {
                AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                Resources = new List<string>(),
            }
        );
    }

    private void InvokeOnStop(OnChangeActions onChangeActions)
    {
        coordinator.SetStoppedDueToInternalErrorFlag();
        coordinator.CancelPendingStart();
        follower.CancelPendingStart();
        try
        {
            foreach (var onErrorAction in onChangeActions.OnStopActions)
            {
                onErrorAction.Invoke();
            }
        }
        catch (Exception ex)
        {
            logger.Error(clientId.ToString(), ex.ToString());
        }

        // PRB-0005: the handlers are stopped, so the client no longer reports its old assignment
        store.SetResources(
            new SetResourcesRequest
            {
                AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                Resources = new List<string>(),
            }
        );
    }

    private Task StartLeadershipTask(CancellationToken token, BlockingCollection<ClientEvent> clientEvents) =>
        Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        AcquireLeaseRequest request = new()
                        {
                            ClientId = clientId,
                            ResourceGroup = currentResourceGroup,
                        };
                        var response = await TryAcquireLeaseAsync(request, token);
                        if (response.Result == LeaseResult.Granted) // is now the Coordinator
                        {
                            RebalancerTelemetry.RecordElection(backend, currentResourceGroup);
                            await ExecuteLeaseRenewals(token, clientEvents, response.Lease);
                        }
                        else if (response.Result == LeaseResult.Denied) // is a Follower
                        {
                            PostFollowerEvent(response.Lease.ExpiryPeriod, clientEvents);
                            await WaitFor(GetInterval(response.Lease.HeartbeatPeriod), token);
                        }
                        else if (response.Result == LeaseResult.NoLease)
                        {
                            throw new RebalancerException($"The resource group {currentResourceGroup} does not exist.");
                        }
                        else if (response.IsErrorResponse())
                        {
                            throw new RebalancerException("An non-recoverable error occurred.", response.Exception);
                        }
                        else
                        {
                            throw new RebalancerException(
                                "A non-supported lease result was received"
                            ); // should never happen, just in case I screw up in the future
                        }
                    }
                }
                finally
                {
                    clientEvents.CompleteAdding();
                }
            }
        );

    private async Task ExecuteLeaseRenewals(
        CancellationToken token,
        BlockingCollection<ClientEvent> clientEvents,
        Lease lease
    )
    {
        CoordinatorToken coordinatorToken = new();
        leaseFencingToken = lease.FencingToken;
        PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
        await WaitFor(GetInterval(lease.HeartbeatPeriod), token, coordinatorToken);

        // lease renewal loop
        while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
        {
            var response = await TryRenewLeaseAsync(
                new RenewLeaseRequest
                {
                    ClientId = clientId,
                    ResourceGroup = currentResourceGroup,
                    FencingToken = lease.FencingToken,
                },
                token
            );
            if (response.Result == LeaseResult.Granted)
            {
                PostLeaderEvent(lease.FencingToken, lease.ExpiryPeriod, coordinatorToken, clientEvents);
                await WaitFor(GetInterval(lease.HeartbeatPeriod), token, coordinatorToken);
            }
            else if (response.Result == LeaseResult.Denied)
            {
                PostFollowerEvent(lease.ExpiryPeriod, clientEvents);
                await WaitFor(GetInterval(lease.HeartbeatPeriod), token);
                break;
            }
            else if (response.Result == LeaseResult.NoLease)
            {
                throw new RebalancerException($"The resource group {currentResourceGroup} does not exist.");
            }
            else if (response.IsErrorResponse())
            {
                throw new RebalancerException("An non-recoverable error occurred.", response.Exception);
            }
            else
            {
                throw new RebalancerException(
                    "A non-supported lease result was received"
                ); // should never happen, just in case I screw up in the future
            }
        }
    }

    private async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request, CancellationToken token)
    {
        // up to three tries, waiting 2s and then 4s between them
        var delaySeconds = 2;
        var response = await leaseService.TryAcquireLeaseAsync(request);
        for (var retriesLeft = 2; retriesLeft > 0 && response.Result == LeaseResult.TransientError; retriesLeft--)
        {
            await WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
            delaySeconds *= 2;
            response = await leaseService.TryAcquireLeaseAsync(request);
        }

        return response;
    }

    private async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request, CancellationToken token)
    {
        // up to three tries, waiting 2s and then 4s between them
        var delaySeconds = 2;
        var response = await leaseService.TryRenewLeaseAsync(request);
        for (var retriesLeft = 2; retriesLeft > 0 && response.Result == LeaseResult.TransientError; retriesLeft--)
        {
            await WaitFor(TimeSpan.FromSeconds(delaySeconds), token);
            delaySeconds *= 2;
            response = await leaseService.TryRenewLeaseAsync(request);
        }

        return response;
    }

    private TimeSpan GetInterval(TimeSpan heartbeatPeriod) =>
        TimeSpan.FromMilliseconds(heartbeatPeriod.TotalMilliseconds / 2.5);

    private void PostLeaderEvent(
        int fencingToken,
        TimeSpan keepAliveExpiryPeriod,
        CoordinatorToken coordinatorToken,
        BlockingCollection<ClientEvent> clientEvents
    )
    {
        logger.Debug(clientId.ToString(), $"{clientId} is leader");
        isCoordinator = true;
        ClientEvent clientEvent = new()
        {
            ResourceGroup = currentResourceGroup,
            EventType = EventType.Coordinator,
            FencingToken = fencingToken,
            CoordinatorToken = coordinatorToken,
            KeepAliveExpiryPeriod = keepAliveExpiryPeriod,
            ClientRecordRetention = ClientOptions.GetClientRecordRetention(currentClientOptions, keepAliveExpiryPeriod),
            OnAssignmentDelay = currentClientOptions.OnAssignmentDelay,
        };
        clientEvents.Add(clientEvent);
    }

    private void PostFollowerEvent(TimeSpan keepAliveExpiryPeriod, BlockingCollection<ClientEvent> clientEvents)
    {
        logger.Debug(clientId.ToString(), $"{clientId} is follower");
        isCoordinator = false;
        ClientEvent clientEvent = new()
        {
            EventType = EventType.Follower,
            ResourceGroup = currentResourceGroup,
            KeepAliveExpiryPeriod = keepAliveExpiryPeriod,
            OnAssignmentDelay = currentClientOptions.OnAssignmentDelay,
        };
        clientEvents.Add(clientEvent);
    }

    // OnAssignmentDelay is applied by the roles, between a grant and its start actions (PRB-0007)
    private Task StartRoleTask(
        CancellationToken token,
        OnChangeActions onChangeActions,
        BlockingCollection<ClientEvent> clientEvents
    ) => Task.Run(async () =>
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
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        follower.CancelPendingStart();

                        await coordinator.ExecuteCoordinatorRoleAsync(clientId, clientEvent, onChangeActions, token);
                    }
                    else
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        coordinator.CancelPendingStart();

                        await follower.ExecuteFollowerRoleAsync(clientId, clientEvent, onChangeActions, token);
                    }
                }
                else
                {
                    await WaitFor(TimeSpan.FromSeconds(1), token);
                }
            }
        }
    );

    private async Task WaitFor(TimeSpan delayPeriod, CancellationToken token, CoordinatorToken coordinatorToken)
    {
        Stopwatch sw = new();
        sw.Start();
        while (!token.IsCancellationRequested && !coordinatorToken.FencingTokenViolation)
        {
            if (sw.Elapsed < delayPeriod)
            {
                await WaitFor(TimeSpan.FromMilliseconds(100), token);
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
            // a client that gave up or stopped is never assigned again: report that instead of waiting (US-0021)
            if (aborted || stopped)
            {
                return GetAssignedResources();
            }

            var response = store.GetResources();
            if (response.AssignmentStatus == AssignmentStatus.ResourcesAssigned ||
                response.AssignmentStatus == AssignmentStatus.NoResourcesAssigned)
            {
                return new AssignedResources
                {
                    Resources = response.Resources,
                    ClientState = GetState(response.AssignmentStatus),
                };
            }

            Thread.Sleep(100);
        }

        return new AssignedResources
        {
            Resources = new List<string>(),
            ClientState = ClientState.Stopped,
        };
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
