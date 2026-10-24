using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer.Roles;

internal class Follower
{
    private readonly IClientService clientService;
    private readonly IRebalancerLogger logger;
    private readonly ResourceGroupStore store;
    private PendingStart pendingStart;

    public Follower(IRebalancerLogger logger, IClientService clientService, ResourceGroupStore store)
    {
        this.logger = logger;
        this.clientService = clientService;
        this.store = store;
    }

    /// <summary>Forgets a grant whose start is still delayed (PRB-0007), e.g. when the node changes role.</summary>
    public void CancelPendingStart() => pendingStart = null;

    public async Task ExecuteFollowerRoleAsync(
        Guid followerClientId,
        ClientEvent clientEvent,
        OnChangeActions onChangeActions,
        CancellationToken token
    )
    {
        var (self, _) = await KeepAliveOrReRegisterAsync(followerClientId, clientEvent.ResourceGroup, onChangeActions);
        logger.Debug(
            followerClientId.ToString(),
            $"FOLLOWER : Keep Alive sent. Coordinator: {self.CoordinatorStatus} Client: {self.ClientStatus}"
        );
        // PRB-0007: OnAssignmentDelay runs between seeing a grant and starting it, across ticks, so keep-alives and stop
        // requests are never delayed; the start happens only if the record still grants the same resources then
        var grantWaiting = self.CoordinatorStatus == CoordinatorStatus.ResourcesGranted &&
                           self.ClientStatus == ClientStatus.Waiting;
        if (!grantWaiting)
        {
            pendingStart = null;
        }
        else if (clientEvent.OnAssignmentDelay > TimeSpan.Zero)
        {
            if (pendingStart == null || !pendingStart.Matches(self.AssignedResources, self.FencingToken))
            {
                pendingStart = new PendingStart(self.AssignedResources, self.FencingToken);
                logger.Debug(
                    followerClientId.ToString(),
                    $"FOLLOWER : Delaying on start for {(int)clientEvent.OnAssignmentDelay.TotalMilliseconds}ms"
                );
                return;
            }

            if (pendingStart.Elapsed < clientEvent.OnAssignmentDelay)
            {
                return;
            }

            pendingStart = null;
        }

        if (self.CoordinatorStatus == CoordinatorStatus.StopActivity)
        {
            if (self.ClientStatus == ClientStatus.Active)
            {
                logger.Info(followerClientId.ToString(), "-------------- Stopping activity ---------------");
                logger.Debug(followerClientId.ToString(), "FOLLOWER : Invoking on stop actions");
                foreach (var stopAction in onChangeActions.OnStopActions)
                {
                    stopAction.Invoke();
                }

                store.SetResources(
                    new SetResourcesRequest
                    {
                        AssignmentStatus = AssignmentStatus.AssignmentInProgress,
                        Resources = new List<string>(),
                    }
                );
                await clientService.SetClientStatusAsync(followerClientId, ClientStatus.Waiting);
                logger.Info(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus} -> WAITING");
            }
            else
            {
                logger.Debug(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus}");
            }
        }
        else if (self.CoordinatorStatus == CoordinatorStatus.ResourcesGranted)
        {
            if (self.ClientStatus == ClientStatus.Waiting)
            {
                if (self.AssignedResources.Any())
                {
                    store.SetResources(
                        new SetResourcesRequest
                        {
                            AssignmentStatus = AssignmentStatus.ResourcesAssigned,
                            Resources = self.AssignedResources,
                        }
                    );
                }
                else
                {
                    store.SetResources(
                        new SetResourcesRequest
                        {
                            AssignmentStatus = AssignmentStatus.NoResourcesAssigned,
                            Resources = new List<string>(),
                        }
                    );
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await clientService.SetClientStatusAsync(followerClientId, ClientStatus.Active);

                if (self.AssignedResources.Any())
                {
                    logger.Info(
                        followerClientId.ToString(),
                        $"FOLLOWER : Granted resources={string.Join(",", self.AssignedResources)}"
                    );
                }
                else
                {
                    logger.Info(followerClientId.ToString(), "FOLLOWER : No resources available to be assigned.");
                }

                foreach (var startAction in onChangeActions.OnStartActions)
                {
                    startAction.Invoke(self.AssignedResources.Any() ? self.AssignedResources : new List<string>());
                }

                logger.Info(followerClientId.ToString(), $"FOLLOWER : State={self.ClientStatus} -> ACTIVE");
                logger.Info(followerClientId.ToString(), "-------------- Activity started ---------------");
            }
            else
            {
                logger.Debug(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus}");
            }
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
