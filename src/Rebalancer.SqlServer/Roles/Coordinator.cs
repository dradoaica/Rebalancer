namespace Rebalancer.SqlServer.Roles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clients;
using Core;
using Core.Logging;
using Resources;
using Store;

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

    public Coordinator(IRebalancerLogger logger,
        IResourceService resourceService,
        IClientService clientService,
        ResourceGroupStore store)
    {
        this.logger = logger;
        this.resourceService = resourceService;
        this.clientService = clientService;
        this.store = store;
        this.resources = new List<string>();
        this.clients = new List<Guid>();
    }

    public int GetFencingToken() => this.currentFencingToken;

    public void SetStoppedDueToInternalErrorFlag() => this.stoppedDueToInternalErrorFlag = true;

    public async Task ExecuteCoordinatorRoleAsync(Guid coordinatorClientId,
        ClientEvent clientEvent,
        OnChangeActions onChangeActions,
        CancellationToken token)
    {
        this.currentFencingToken = clientEvent.FencingToken;
        var self = await this.clientService.KeepAliveAsync(coordinatorClientId);
        var resourcesNow = (await this.resourceService.GetResourcesAsync(clientEvent.ResourceGroup))
            .OrderBy(x => x)
            .ToList();
        var clientsNow = await this.GetLiveClientsAsync(clientEvent, coordinatorClientId);
        var clientIds = clientsNow.Select(x => x.ClientId).ToList();
        clientIds.Add(coordinatorClientId);

        if (clientsNow.Any(x => x.FencingToken > clientEvent.FencingToken))
        {
            clientEvent.CoordinatorToken.FencingTokenViolation = true;
            return;
        }

        if (self.ClientStatus == ClientStatus.Terminated || this.stoppedDueToInternalErrorFlag)
        {
            this.stoppedDueToInternalErrorFlag = false;
            await this.clientService.SetClientStatusAsync(coordinatorClientId, ClientStatus.Waiting);
            this.logger.Debug(coordinatorClientId.ToString(),
                "Status change: COORDINATOR was terminated due to an error");
            await this.TriggerRebalancingAsync(coordinatorClientId, clientEvent, clientsNow, resourcesNow,
                onChangeActions,
                token);
        }
        else if (!this.resources.OrderBy(x => x).SequenceEqual(resourcesNow.OrderBy(x => x)))
        {
            this.logger.Debug(coordinatorClientId.ToString(),
                $"Resource change: Old: {string.Join(",", this.resources.OrderBy(x => x))} New: {string.Join(",", resourcesNow.OrderBy(x => x))}");
            await this.TriggerRebalancingAsync(coordinatorClientId, clientEvent, clientsNow, resourcesNow,
                onChangeActions,
                token);
        }
        else if (!this.clients.OrderBy(x => x).SequenceEqual(clientIds.OrderBy(x => x)))
        {
            this.logger.Debug(coordinatorClientId.ToString(),
                $"Client change: Old: {string.Join(",", this.clients.OrderBy(x => x))} New: {string.Join(",", clientIds.OrderBy(x => x))}");
            await this.TriggerRebalancingAsync(coordinatorClientId, clientEvent, clientsNow, resourcesNow,
                onChangeActions,
                token);
        }
    }

    public int GetCurrentFencingToken() => this.currentFencingToken;

    private async Task<List<Client>> GetLiveClientsAsync(ClientEvent clientEvent, Guid coordinatorClientId)
    {
        var allClientsNow = (await this.clientService.GetActiveClientsAsync(clientEvent.ResourceGroup))
            .Where(x => x.ClientId != coordinatorClientId)
            .ToList();

        var liveClientsNow = allClientsNow
            .Where(x => x.TimeNow - x.LastKeepAlive < clientEvent.KeepAliveExpiryPeriod).ToList();

        return liveClientsNow;
    }

    private async Task TriggerRebalancingAsync(Guid coordinatorClientId,
        ClientEvent clientEvent,
        List<Client> clients,
        List<string> resources,
        OnChangeActions onChangeActions,
        CancellationToken token)
    {
        this.logger.Info(coordinatorClientId.ToString(), "---------- Rebalancing triggered -----------");

        // request stop of all clients
        this.logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Requested stop");
        if (clients.Any())
        {
            var result = await this.clientService.StopActivityAsync(clientEvent.FencingToken, clients);
            if (result == ModifyClientResult.FencingTokenViolation)
            {
                clientEvent.CoordinatorToken.FencingTokenViolation = true;
                return;
            }

            if (result == ModifyClientResult.Error)
            {
                this.logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return;
            }
        }

        // stop all resource activity in local coordinator client
        foreach (var onStopAction in onChangeActions.OnStopActions)
        {
            onStopAction.Invoke();
        }

        // wait for all live clients to confirm stopped
        var allClientsWaiting = false;
        var allClientsWaitingCheckCount = 0;
        List<Client> clientsNow = null;
        while (!allClientsWaiting && !token.IsCancellationRequested)
        {
            allClientsWaitingCheckCount++;
            await this.WaitFor(TimeSpan.FromSeconds(5), token);
            clientsNow = await this.GetLiveClientsAsync(clientEvent, coordinatorClientId);
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
                this.logger.Warn(coordinatorClientId.ToString(),
                    "COORDINATOR: May be stuck waiting for all live clients to confirm stopped, request stop of the remaining clients!!!");
                var clientsRemaining =
                    clientsNow.Where(x => x.ClientStatus != ClientStatus.Waiting).ToList();
                if (clientsRemaining.Any())
                {
                    var result =
                        await this.clientService.StopActivityAsync(clientEvent.FencingToken, clientsRemaining);
                    if (result == ModifyClientResult.FencingTokenViolation)
                    {
                        clientEvent.CoordinatorToken.FencingTokenViolation = true;
                        return;
                    }

                    if (result == ModifyClientResult.Error)
                    {
                        this.logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                        return;
                    }
                }
            }
        }

        this.logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Stop confirmed");

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

            ClientStartRequest coordinatorRequest = new() {ClientId = coordinatorClientId};
            while (coordinatorRequest.AssignedResources.Count < resourcesPerClient && resourcesToAssign.Any())
            {
                coordinatorRequest.AssignedResources.Add(resourcesToAssign.Dequeue());
            }

            clientStartRequests.Add(coordinatorRequest);
            remainingClients--;

            foreach (var client in clientsNow)
            {
                resourcesPerClient = Math.Max(1, resourcesToAssign.Count / remainingClients);

                ClientStartRequest request = new() {ClientId = client.ClientId};

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

            this.logger.Info(coordinatorClientId.ToString(), "COORDINATOR: Resources assigned");
            var startResult =
                await this.clientService.StartActivityAsync(clientEvent.FencingToken, clientStartRequests);
            if (startResult == ModifyClientResult.FencingTokenViolation)
            {
                clientEvent.CoordinatorToken.FencingTokenViolation = true;
                return;
            }

            if (startResult == ModifyClientResult.Error)
            {
                this.logger.Error(coordinatorClientId.ToString(), "COORDINATOR: Rebalancing error");
                return;
            }

            this.store.SetResources(new SetResourcesRequest
            {
                AssignmentStatus = AssignmentStatus.ResourcesAssigned,
                Resources = coordinatorRequest.AssignedResources
            });
            foreach (var onStartAction in onChangeActions.OnStartActions)
            {
                onStartAction.Invoke(coordinatorRequest.AssignedResources);
            }

            this.logger.Debug(coordinatorClientId.ToString(), "COORDINATOR: Local client started");

            var clientIds = clientsNow.Select(x => x.ClientId).ToList();
            clientIds.Add(coordinatorClientId);
            this.clients = clientIds;
            this.resources = resources;
            this.logger.Info(coordinatorClientId.ToString(), "---------- Activity Started -----------");
        }
        else
        {
            // log it
            this.logger.Info(coordinatorClientId.ToString(), "!!!");
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
}
