using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Roles;

internal class Coordinator
{
    private readonly IClientService clientService;
    private readonly IRebalancerLogger logger;
    private readonly IResourceService resourceService;
    private readonly ResourceGroupStore store;
    // ADR-0026: the fencing token of the last completed rebalance; a new tenure is a change like any other
    private int balancedFencingToken;
    private List<Guid> clients;
    private int currentFencingToken;

    private List<string> resources;
    private bool stoppedDueToInternalErrorFlag;

    public Coordinator(
        IRebalancerLogger logger,
        IResourceService resourceService,
        IClientService clientService,
        ResourceGroupStore store
    )
    {
        this.logger = logger;
        this.resourceService = resourceService;
        this.clientService = clientService;
        this.store = store;
        resources = new List<string>();
        clients = new List<Guid>();
    }

    public int GetFencingToken() => currentFencingToken;

    public void SetStoppedDueToInternalErrorFlag() => stoppedDueToInternalErrorFlag = true;

    public async Task ExecuteCoordinatorRoleAsync(
        Guid coordinatorClientId,
        ClientEvent clientEvent,
        OnChangeActions onChangeActions,
        CancellationToken token
    )
    {
        currentFencingToken = clientEvent.FencingToken;
        var (self, reRegistered) = await KeepAliveOrReRegisterAsync(
            coordinatorClientId,
            clientEvent.ResourceGroup,
            onChangeActions
        );
        if (reRegistered)
        {
            // its local activity was stopped, so the group must be rebalanced
            stoppedDueToInternalErrorFlag = true;
        }

        // ADR-0026: act only while the lease names this client with the event's token; the event may have been queued
        // before a stall longer than the lease expiry
        if (clientService is IFencedClientService fenced &&
            !await fenced.HoldsLeaseAsync(clientEvent.ResourceGroup, coordinatorClientId, clientEvent.FencingToken))
        {
            await StepDownAsync(coordinatorClientId, clientEvent, onChangeActions);
            return;
        }

        var resourcesNow = (await resourceService.GetResourcesAsync(clientEvent.ResourceGroup)).OrderBy(x => x)
            .ToList();
        var clientsNow = await GetLiveClientsAsync(clientEvent, coordinatorClientId);
        var clientIds = clientsNow.Select(x => x.ClientId).ToList();
        clientIds.Add(coordinatorClientId);

        if (clientsNow.Any(x => x.FencingToken > clientEvent.FencingToken))
        {
            await StepDownAsync(coordinatorClientId, clientEvent, onChangeActions);
            return;
        }

        // ADR-0014: expire records of nodes that stopped sending keep-alives long ago (guarded by the fencing token).
        // Housekeeping only: a failure (e.g. a SQL login without DELETE) must not stop coordination.
        try
        {
            await clientService.DeleteExpiredClientsAsync(
                clientEvent.ResourceGroup,
                coordinatorClientId,
                clientEvent.FencingToken,
                clientEvent.ClientRecordRetention
            );
        }
        catch (Exception e)
        {
            logger.Warn(
                coordinatorClientId.ToString(),
                $"COORDINATOR: Could not expire stale client records: {e.Message}"
            );
        }

        if (self.ClientStatus == ClientStatus.Terminated || stoppedDueToInternalErrorFlag)
        {
            stoppedDueToInternalErrorFlag = false;
            await clientService.SetClientStatusAsync(coordinatorClientId, ClientStatus.Waiting);
            logger.Debug(coordinatorClientId.ToString(), "Status change: COORDINATOR was terminated due to an error");
            await TriggerRebalancingAsync(
                coordinatorClientId,
                clientEvent,
                clientsNow,
                resourcesNow,
                onChangeActions,
                token
            );
        }
        else if (!resources.OrderBy(x => x).SequenceEqual(resourcesNow.OrderBy(x => x)))
        {
            logger.Debug(
                coordinatorClientId.ToString(),
                $"Resource change: Old: {string.Join(",", resources.OrderBy(x => x))} New: {string.Join(",", resourcesNow.OrderBy(x => x))}"
            );
            await TriggerRebalancingAsync(
                coordinatorClientId,
                clientEvent,
                clientsNow,
                resourcesNow,
                onChangeActions,
                token
            );
        }
        else if (!clients.OrderBy(x => x).SequenceEqual(clientIds.OrderBy(x => x)))
        {
            logger.Debug(
                coordinatorClientId.ToString(),
                $"Client change: Old: {string.Join(",", clients.OrderBy(x => x))} New: {string.Join(",", clientIds.OrderBy(x => x))}"
            );
            await TriggerRebalancingAsync(
                coordinatorClientId,
                clientEvent,
                clientsNow,
                resourcesNow,
                onChangeActions,
                token
            );
        }
        else if (balancedFencingToken != clientEvent.FencingToken)
        {
            logger.Debug(
                coordinatorClientId.ToString(),
                $"Tenure change: Old fencing token: {balancedFencingToken} New: {clientEvent.FencingToken}"
            );
            await TriggerRebalancingAsync(
                coordinatorClientId,
                clientEvent,
                clientsNow,
                resourcesNow,
                onChangeActions,
                token
            );
        }
        else if (clientsNow.Any(x =>
                     x.ClientStatus == ClientStatus.Waiting && x.CoordinatorStatus == CoordinatorStatus.StopActivity
                 ))
        {
            // PRB-0005: a client that is stopped and not granted needs an assignment, e.g. one that restarted after an
            // error between two ticks of this coordinator
            logger.Debug(coordinatorClientId.ToString(), "Client waiting for an assignment");
            await TriggerRebalancingAsync(
                coordinatorClientId,
                clientEvent,
                clientsNow,
                resourcesNow,
                onChangeActions,
                token
            );
        }
    }

    public int GetCurrentFencingToken() => currentFencingToken;

    private async Task<List<Client>> GetLiveClientsAsync(ClientEvent clientEvent, Guid coordinatorClientId)
    {
        var allClientsNow = (await clientService.GetActiveClientsAsync(clientEvent.ResourceGroup))
            .Where(x => x.ClientId != coordinatorClientId)
            .ToList();

        var liveClientsNow = allClientsNow.Where(x => x.TimeNow - x.LastKeepAlive < clientEvent.KeepAliveExpiryPeriod)
            .ToList();

        return liveClientsNow;
    }

    // telemetry (ADR-0022): one activity and one outcome per rebalance; an exception counts as aborted
    private async Task TriggerRebalancingAsync(
        Guid coordinatorClientId,
        ClientEvent clientEvent,
        List<Client> clients,
        List<string> resources,
        OnChangeActions onChangeActions,
        CancellationToken token
    )
    {
        using var activity = RebalancerTelemetry.StartRebalance(
            "redis",
            clientEvent.ResourceGroup,
            RebalancerTelemetry.FencingTokenTag,
            clientEvent.FencingToken
        );
        var completed = false;
        try
        {
            completed = await RebalanceAsync(
                coordinatorClientId,
                clientEvent,
                clients,
                resources,
                onChangeActions,
                token
            );
        }
        finally
        {
            RebalancerTelemetry.RecordRebalance("redis", clientEvent.ResourceGroup, completed);
        }
    }

    // returns true when resources were granted, false when the rebalance stopped early
    private async Task<bool> RebalanceAsync(
        Guid coordinatorClientId,
        ClientEvent clientEvent,
        List<Client> clients,
        List<string> resources,
        OnChangeActions onChangeActions,
        CancellationToken token
    )
    {
        logger.Info(coordinatorClientId.ToString(), "---------- Rebalancing triggered -----------");

        // request stop of all clients
        logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Requested stop");
        if (clients.Any())
        {
            var result = await StopActivityAsync(coordinatorClientId, clientEvent, clients);
            if (result == ModifyClientResult.FencingTokenViolation)
            {
                await StepDownAsync(coordinatorClientId, clientEvent, onChangeActions);
                return false;
            }

            if (result == ModifyClientResult.Error)
            {
                logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return false;
            }
        }

        await StopUnresponsiveClientsAsync(coordinatorClientId, clientEvent);

        // stop all resource activity in local coordinator client, and record it like a follower would: a successor
        // coordinator's stop barrier then waits for this node too if it loses the lease before resources are granted
        foreach (var onStopAction in onChangeActions.OnStopActions)
        {
            onStopAction.Invoke();
        }

        store.SetResources(
            new SetResourcesRequest
            {
                AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                Resources = new List<string>(),
            }
        );
        await clientService.SetClientStatusAsync(coordinatorClientId, ClientStatus.Waiting);

        // wait for all live clients to confirm stopped
        var allClientsWaiting = false;
        var allClientsWaitingCheckCount = 0;
        List<Client> clientsNow = null;
        while (!allClientsWaiting && !token.IsCancellationRequested)
        {
            allClientsWaitingCheckCount++;
            await WaitFor(TimeSpan.FromSeconds(5), token);
            clientsNow = await GetLiveClientsAsync(clientEvent, coordinatorClientId);
            if (!clientsNow.Any())
            {
                allClientsWaiting = true;
            }
            else
            {
                allClientsWaiting = clientsNow.All(x => x.ClientStatus == ClientStatus.Waiting);
            }

            if (allClientsWaitingCheckCount % 12 == 0)
            {
                logger.Warn(
                    coordinatorClientId.ToString(),
                    "COORDINATOR: May be stuck waiting for all live clients to confirm stopped, request stop of the remaining clients!!!"
                );
                var clientsRemaining = clientsNow.Where(x => x.ClientStatus != ClientStatus.Waiting).ToList();
                if (clientsRemaining.Any())
                {
                    var result = await StopActivityAsync(coordinatorClientId, clientEvent, clientsRemaining);
                    if (result == ModifyClientResult.FencingTokenViolation)
                    {
                        await StepDownAsync(coordinatorClientId, clientEvent, onChangeActions);
                        return false;
                    }

                    if (result == ModifyClientResult.Error)
                    {
                        logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                        return false;
                    }
                }
            }
        }

        logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Stop confirmed");

        // assign resources first to coordinator then to other live clients
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (allClientsWaiting)
        {
            Queue<string> resourcesToAssign = new(resources);
            List<ClientStartRequest> clientStartRequests = new();
            var remainingClients = clientsNow.Count + 1;
            var resourcesPerClient = Math.Max(1, resourcesToAssign.Count / remainingClients);

            ClientStartRequest coordinatorRequest = new()
            {
                ClientId = coordinatorClientId,
            };
            while (coordinatorRequest.AssignedResources.Count < resourcesPerClient && resourcesToAssign.Any())
            {
                coordinatorRequest.AssignedResources.Add(resourcesToAssign.Dequeue());
            }

            clientStartRequests.Add(coordinatorRequest);
            remainingClients--;

            foreach (var client in clientsNow)
            {
                resourcesPerClient = Math.Max(1, resourcesToAssign.Count / remainingClients);

                ClientStartRequest request = new()
                {
                    ClientId = client.ClientId,
                };

                while (request.AssignedResources.Count < resourcesPerClient && resourcesToAssign.Any())
                {
                    request.AssignedResources.Add(resourcesToAssign.Dequeue());
                }

                clientStartRequests.Add(request);
                remainingClients--;
            }

            if (token.IsCancellationRequested)
            {
                return false;
            }

            logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Resources assigned");
            var startResult = await StartActivityAsync(coordinatorClientId, clientEvent, clientStartRequests);
            if (startResult == ModifyClientResult.FencingTokenViolation)
            {
                await StepDownAsync(coordinatorClientId, clientEvent, onChangeActions);
                return false;
            }

            if (startResult == ModifyClientResult.Error)
            {
                logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return false;
            }

            store.SetResources(
                new SetResourcesRequest
                {
                    AssignmentStatus = AssignmentStatus.ResourcesAssigned,
                    Resources = coordinatorRequest.AssignedResources,
                }
            );

            // mark the local client active so that, if this node is demoted, the next coordinator stops it
            await clientService.SetClientStatusAsync(coordinatorClientId, ClientStatus.Active);
            foreach (var onStartAction in onChangeActions.OnStartActions)
            {
                onStartAction.Invoke(coordinatorRequest.AssignedResources);
            }

            logger.Debug(coordinatorClientId.ToString(), "COORDINATOR: Local client started");

            var clientIds = clientsNow.Select(x => x.ClientId).ToList();
            clientIds.Add(coordinatorClientId);
            this.clients = clientIds;
            this.resources = resources;
            balancedFencingToken = clientEvent.FencingToken;
            logger.Info(coordinatorClientId.ToString(), "---------- Activity Started -----------");
            return true;
        }

        // log it
        logger.Info(coordinatorClientId.ToString(), "!!!");
        return false;
    }

    // ADR-0026: the one way a fenced-out coordinator (lost lease, or a newer token on a record) steps down. It stops its
    // own handlers and records Waiting, as at the start of a rebalance (PRB-0001), so a newer coordinator's barrier
    // waits for this node too; the leadership task then asks for the lease again.
    private async Task StepDownAsync(Guid coordinatorClientId, ClientEvent clientEvent, OnChangeActions onChangeActions)
    {
        clientEvent.CoordinatorToken.FencingTokenViolation = true;
        logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Fenced out by a newer coordinator, stepping down");
        if (store.GetResources().AssignmentStatus != AssignmentStatus.AssignmentInProgress)
        {
            foreach (var onStopAction in onChangeActions.OnStopActions)
            {
                onStopAction.Invoke();
            }

            store.SetResources(
                new SetResourcesRequest
                {
                    AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                    Resources = new List<string>(),
                }
            );
        }

        await clientService.SetClientStatusAsync(coordinatorClientId, ClientStatus.Waiting);
    }

    // ADR-0026: coordinator writes carry the lease guard where the client service supports it
    private Task<ModifyClientResult> StopActivityAsync(
        Guid coordinatorClientId,
        ClientEvent clientEvent,
        List<Client> targets
    ) =>
        clientService is IFencedClientService fenced
            ? fenced.StopActivityAsync(clientEvent.ResourceGroup, coordinatorClientId, clientEvent.FencingToken, targets)
            : clientService.StopActivityAsync(clientEvent.FencingToken, targets);

    private Task<ModifyClientResult> StartActivityAsync(
        Guid coordinatorClientId,
        ClientEvent clientEvent,
        List<ClientStartRequest> requests
    ) =>
        clientService is IFencedClientService fenced
            ? fenced.StartActivityAsync(
                clientEvent.ResourceGroup,
                coordinatorClientId,
                clientEvent.FencingToken,
                requests
            ) : clientService.StartActivityAsync(clientEvent.FencingToken, requests);

    private async Task WaitFor(TimeSpan waitPeriod, CancellationToken rebalancingToken)
    {
        try
        {
            await Task.Delay(waitPeriod, rebalancingToken);
        }
        catch (TaskCanceledException)
        {
        }
    }

    // ADR-0014: a record can expire while this node is paused. The node may still run resources that were reassigned
    // since, so it stops them before it registers again (the same safety rule as for a demoted coordinator).
    private async Task<(Client Self, bool ReRegistered)> KeepAliveOrReRegisterAsync(
        Guid clientId,
        string resourceGroup,
        OnChangeActions onChangeActions
    )
    {
        try
        {
            return (await clientService.KeepAliveAsync(clientId), false);
        }
        catch (ClientNotFoundException)
        {
            logger.Warn(clientId.ToString(), "Client record expired: stopping local activity and registering again");
            if (store.GetResources().AssignmentStatus != AssignmentStatus.AssignmentInProgress)
            {
                foreach (var onStopAction in onChangeActions.OnStopActions)
                {
                    onStopAction.Invoke();
                }
            }

            store.SetResources(
                new SetResourcesRequest
                {
                    AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                    Resources = new List<string>(),
                }
            );
            await clientService.CreateClientAsync(resourceGroup, clientId);
            return (await clientService.KeepAliveAsync(clientId), true);
        }
    }

    // ADR-0019 (PRB-0003): a client whose keep-alive expired may only be paused. If its record still grants resources,
    // a resumed follower would keep them although they are reassigned below; recording StopActivity makes it stop at
    // its first tick through the normal follower path. A violation here is not fatal: a row can disappear between the
    // read and the stop (e.g. a clean stop deletes it), and a real fencing violation is caught again when resources are
    // granted with the same fencing token.
    private async Task StopUnresponsiveClientsAsync(Guid coordinatorClientId, ClientEvent clientEvent)
    {
        var unresponsive = (await clientService.GetActiveClientsAsync(clientEvent.ResourceGroup)).Where(x =>
                x.ClientId != coordinatorClientId &&
                x.TimeNow - x.LastKeepAlive >= clientEvent.KeepAliveExpiryPeriod &&
                x.CoordinatorStatus == CoordinatorStatus.ResourcesGranted
            )
            .ToList();
        if (!unresponsive.Any())
        {
            return;
        }

        var result = await StopActivityAsync(coordinatorClientId, clientEvent, unresponsive);
        if (result != ModifyClientResult.Ok)
        {
            logger.Warn(
                coordinatorClientId.ToString(),
                $"COORDINATOR: Could not record stop for unresponsive clients ({result})"
            );
        }
    }
}
