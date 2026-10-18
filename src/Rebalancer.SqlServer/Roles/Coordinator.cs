using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Resources;
using Rebalancer.SqlServer.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer.Roles;

internal class Coordinator
{
    private readonly IClientService clientService;
    private readonly IRebalancerLogger logger;
    private readonly IResourceService resourceService;
    private readonly ResourceGroupStore store;
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
        var (self, reRegistered) =
            await KeepAliveOrReRegisterAsync(coordinatorClientId, clientEvent.ResourceGroup, onChangeActions);
        if (reRegistered)
        {
            // its local activity was stopped, so the group must be rebalanced
            stoppedDueToInternalErrorFlag = true;
        }

        var resourcesNow = (await resourceService.GetResourcesAsync(clientEvent.ResourceGroup)).OrderBy(x => x)
            .ToList();
        var clientsNow = await GetLiveClientsAsync(clientEvent, coordinatorClientId);
        var clientIds = clientsNow.Select(x => x.ClientId).ToList();
        clientIds.Add(coordinatorClientId);

        if (clientsNow.Any(x => x.FencingToken > clientEvent.FencingToken))
        {
            clientEvent.CoordinatorToken.FencingTokenViolation = true;
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
            logger.Warn(coordinatorClientId.ToString(), $"COORDINATOR: Could not expire stale client records: {e.Message}");
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

    private async Task TriggerRebalancingAsync(
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
            var result = await clientService.StopActivityAsync(clientEvent.FencingToken, clients);
            if (result == ModifyClientResult.FencingTokenViolation)
            {
                clientEvent.CoordinatorToken.FencingTokenViolation = true;
                return;
            }

            if (result == ModifyClientResult.Error)
            {
                logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return;
            }
        }

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
                    var result = await clientService.StopActivityAsync(clientEvent.FencingToken, clientsRemaining);
                    if (result == ModifyClientResult.FencingTokenViolation)
                    {
                        clientEvent.CoordinatorToken.FencingTokenViolation = true;
                        return;
                    }

                    if (result == ModifyClientResult.Error)
                    {
                        logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                        return;
                    }
                }
            }
        }

        logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Stop confirmed");

        // assign resources first to coordinator then to other live clients
        if (token.IsCancellationRequested)
        {
            return;
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
                return;
            }

            logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Resources assigned");
            var startResult = await clientService.StartActivityAsync(clientEvent.FencingToken, clientStartRequests);
            if (startResult == ModifyClientResult.FencingTokenViolation)
            {
                clientEvent.CoordinatorToken.FencingTokenViolation = true;
                return;
            }

            if (startResult == ModifyClientResult.Error)
            {
                logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return;
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
            logger.Info(coordinatorClientId.ToString(), "---------- Activity Started -----------");
        }
        else
        {
            // log it
            logger.Info(coordinatorClientId.ToString(), "!!!");
        }
    }

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
}
