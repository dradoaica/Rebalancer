namespace Rebalancer.SqlServer.Roles;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clients;
using Core;
using Core.Logging;
using Store;

internal class Follower
{
    private readonly IClientService clientService;
    private readonly IRebalancerLogger logger;
    private readonly ResourceGroupStore store;

    public Follower(IRebalancerLogger logger,
        IClientService clientService,
        ResourceGroupStore store)
    {
        this.logger = logger;
        this.clientService = clientService;
        this.store = store;
    }

    public async Task ExecuteFollowerRoleAsync(Guid followerClientId,
        ClientEvent clientEvent,
        OnChangeActions onChangeActions,
        CancellationToken token)
    {
        var self = await this.clientService.KeepAliveAsync(followerClientId);
        this.logger.Debug(followerClientId.ToString(),
            $"FOLLOWER : Keep Alive sent. Coordinator: {self.CoordinatorStatus} Client: {self.ClientStatus}");
        if (self.CoordinatorStatus == CoordinatorStatus.StopActivity)
        {
            if (self.ClientStatus == ClientStatus.Active)
            {
                this.logger.Info(followerClientId.ToString(), "-------------- Stopping activity ---------------");
                this.logger.Debug(followerClientId.ToString(), "FOLLOWER : Invoking on stop actions");
                foreach (var stopAction in onChangeActions.OnStopActions)
                {
                    stopAction.Invoke();
                }

                this.store.SetResources(new SetResourcesRequest
                {
                    AssignmentStatus = AssignmentStatus.AssignmentInProgress, Resources = new List<string>()
                });
                await this.clientService.SetClientStatusAsync(followerClientId, ClientStatus.Waiting);
                this.logger.Info(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus} -> WAITING");
            }
            else
            {
                this.logger.Debug(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus}");
            }
        }
        else if (self.CoordinatorStatus == CoordinatorStatus.ResourcesGranted)
        {
            if (self.ClientStatus == ClientStatus.Waiting)
            {
                if (self.AssignedResources.Any())
                {
                    this.store.SetResources(new SetResourcesRequest
                    {
                        AssignmentStatus = AssignmentStatus.ResourcesAssigned, Resources = self.AssignedResources
                    });
                }
                else
                {
                    this.store.SetResources(new SetResourcesRequest
                    {
                        AssignmentStatus = AssignmentStatus.NoResourcesAssigned, Resources = new List<string>()
                    });
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await this.clientService.SetClientStatusAsync(followerClientId, ClientStatus.Active);

                if (self.AssignedResources.Any())
                {
                    this.logger.Info(followerClientId.ToString(),
                        $"FOLLOWER : Granted resources={string.Join(",", self.AssignedResources)}");
                }
                else
                {
                    this.logger.Info(followerClientId.ToString(), "FOLLOWER : No resources available to be assigned.");
                }

                foreach (var startAction in onChangeActions.OnStartActions)
                {
                    startAction.Invoke(self.AssignedResources.Any() ? self.AssignedResources : new List<string>());
                }

                this.logger.Info(followerClientId.ToString(), $"FOLLOWER : State={self.ClientStatus} -> ACTIVE");
                this.logger.Info(followerClientId.ToString(), "-------------- Activity started ---------------");
            }
            else
            {
                this.logger.Debug(followerClientId.ToString(), $"FOLLOWER : State= {self.ClientStatus}");
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
}
