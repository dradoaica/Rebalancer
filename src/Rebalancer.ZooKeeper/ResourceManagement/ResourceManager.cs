namespace Rebalancer.ZooKeeper.ResourceManagement;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Core.Logging;
using Zk;

public class ResourceManager
{
    // immutable state
    private readonly SemaphoreSlim actionsSemaphore = new(1, 1);

    // services
    private readonly IRebalancerLogger logger;
    private readonly OnChangeActions onChangeActions;
    private readonly RebalancingMode rebalancingMode;
    private readonly object resourcesLockObj = new();
    private readonly IZooKeeperService zooKeeperService;
    private AssignmentStatus assignmentStatus;

    // mutable statwe
    private List<string> resources;

    public ResourceManager(IZooKeeperService zooKeeperService,
        IRebalancerLogger logger,
        OnChangeActions onChangeActions,
        RebalancingMode rebalancingMode)
    {
        this.zooKeeperService = zooKeeperService;
        this.logger = logger;
        this.resources = new List<string>();
        this.assignmentStatus = AssignmentStatus.NoAssignmentYet;
        this.onChangeActions = onChangeActions;
        this.rebalancingMode = rebalancingMode;
    }

    public AssignmentStatus GetAssignmentStatus()
    {
        lock (this.resourcesLockObj)
        {
            return this.assignmentStatus;
        }
    }

    public GetResourcesResponse GetResources()
    {
        lock (this.resourcesLockObj)
        {
            if (this.assignmentStatus == AssignmentStatus.ResourcesAssigned)
            {
                return new GetResourcesResponse
                {
                    Resources = new List<string>(this.resources), AssignmentStatus = this.assignmentStatus
                };
            }

            return new GetResourcesResponse {Resources = new List<string>(), AssignmentStatus = this.assignmentStatus};
        }
    }

    private void SetResources(AssignmentStatus newAssignmentStatus, List<string> newResources)
    {
        lock (this.resourcesLockObj)
        {
            this.assignmentStatus = newAssignmentStatus;
            this.resources = new List<string>(newResources);
        }
    }

    public bool IsInStartedState()
    {
        lock (this.resourcesLockObj)
        {
            return this.assignmentStatus == AssignmentStatus.ResourcesAssigned ||
                   this.assignmentStatus == AssignmentStatus.NoResourcesAssigned;
        }
    }

    public async Task InvokeOnStopActionsAsync(string clientId, string role)
    {
        await this.actionsSemaphore.WaitAsync();

        try
        {
            List<string> resourcesToRemove = new(this.GetResources().Resources);
            if (resourcesToRemove.Any())
            {
                this.logger.Info(clientId,
                    $"{role} - Invoking on stop actions. Unassigned resources {string.Join(",", resourcesToRemove)}");

                try
                {
                    foreach (var onStopAction in this.onChangeActions.OnStopActions)
                    {
                        onStopAction.Invoke();
                    }
                }
                catch (Exception e)
                {
                    this.logger.Error(clientId, "{role} - End user on stop actions threw an exception. Terminating. ",
                        e);
                    throw new TerminateClientException("End user on stop actions threw an exception.", e);
                }

                if (this.rebalancingMode == RebalancingMode.ResourceBarrier)
                {
                    try
                    {
                        this.logger.Info(clientId,
                            $"{role} - Removing barriers on resources {string.Join(",", resourcesToRemove)}");
                        var counter = 1;
                        foreach (var resource in resourcesToRemove)
                        {
                            await this.zooKeeperService.RemoveResourceBarrierAsync(resource);

                            if (counter % 10 == 0)
                            {
                                this.logger.Info(clientId,
                                    $"{role} - Removed barriers on {counter} resources of {resourcesToRemove.Count}");
                            }

                            counter++;
                        }

                        this.logger.Info(clientId,
                            $"{role} - Removed barriers on {resourcesToRemove.Count} resources of {resourcesToRemove.Count}");
                    }
                    catch (ZkOperationCancelledException)
                    {
                        // do nothing, cancellation is in progress, these are ephemeral nodes anyway
                    }
                    catch (ZkSessionExpiredException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw new InconsistentStateException("An error occurred while removing resource barriers",
                            e);
                    }
                }

                this.SetResources(AssignmentStatus.NoAssignmentYet, new List<string>());
                this.logger.Info(clientId, $"{role} - On stop complete");
            }
            else
            {
                this.SetResources(AssignmentStatus.NoAssignmentYet, new List<string>());
            }
        }
        finally
        {
            this.actionsSemaphore.Release();
        }
    }

    public async Task InvokeOnStartActionsAsync(string clientId,
        string role,
        List<string> newResources,
        CancellationToken rebalancingToken,
        CancellationToken clientToken)
    {
        await this.actionsSemaphore.WaitAsync();

        if (this.IsInStartedState())
        {
            throw new InconsistentStateException(
                "An attempt to invoke on start actions occurred while already in the started state");
        }

        try
        {
            if (newResources.Any())
            {
                if (this.rebalancingMode == RebalancingMode.ResourceBarrier)
                {
                    try
                    {
                        this.logger.Info(clientId,
                            $"{role} - Putting barriers on resources {string.Join(",", newResources)}");
                        var counter = 1;
                        foreach (var resource in newResources)
                        {
                            await this.zooKeeperService.TryPutResourceBarrierAsync(resource, rebalancingToken,
                                this.logger);

                            if (counter % 10 == 0)
                            {
                                this.logger.Info(clientId,
                                    $"{role} - Put barriers on {counter} resources of {newResources.Count}");
                            }

                            counter++;
                        }

                        this.logger.Info(clientId,
                            $"{role} - Put barriers on {newResources.Count} resources of {newResources.Count}");
                    }
                    catch (ZkOperationCancelledException)
                    {
                        if (clientToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        this.logger.Info(clientId,
                            $"{role} - Rebalancing cancelled, removing barriers on resources {string.Join(",", newResources)}");
                        try
                        {
                            var counter = 1;
                            foreach (var resource in newResources)
                            {
                                await this.zooKeeperService.RemoveResourceBarrierAsync(resource);

                                if (counter % 10 == 0)
                                {
                                    this.logger.Info(clientId,
                                        $"{role} - Removing barriers on {counter} resources of {newResources.Count}");
                                }

                                counter++;
                            }

                            this.logger.Info(clientId,
                                $"{role} - Removed barriers on {newResources.Count} resources of {newResources.Count}");
                        }
                        catch (ZkSessionExpiredException)
                        {
                            throw;
                        }
                        catch (ZkOperationCancelledException)
                        {
                            // do nothing, client cancellation in progress
                        }
                        catch (Exception e)
                        {
                            throw new InconsistentStateException(
                                "An error occurred while removing resource barriers due to rebalancing cancellation",
                                e);
                        }

                        return;
                    }
                    catch (ZkSessionExpiredException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw new InconsistentStateException("An error occurred while putting resource barriers",
                            e);
                    }
                }

                this.SetResources(AssignmentStatus.ResourcesAssigned, newResources);

                try
                {
                    this.logger.Info(clientId,
                        $"{role} - Invoking on start with resources {string.Join(",", this.resources)}");
                    foreach (var onStartAction in this.onChangeActions.OnStartActions)
                    {
                        onStartAction.Invoke(this.resources);
                    }
                }
                catch (Exception e)
                {
                    this.logger.Error(clientId, $"{role} - End user on start actions threw an exception. Terminating. ",
                        e);
                    throw new TerminateClientException("End user on start actions threw an exception.", e);
                }

                this.logger.Info(clientId, $"{role} - On start complete");
            }
            else
            {
                this.SetResources(AssignmentStatus.NoResourcesAssigned, newResources);
            }
        }
        finally
        {
            this.actionsSemaphore.Release();
        }
    }


    public async Task InvokeOnAbortActionsAsync(string clientId, string message, Exception ex = null)
    {
        await this.actionsSemaphore.WaitAsync();
        try
        {
            this.logger.Info(clientId, "Invoking on abort actions.");

            try
            {
                foreach (var onAbortAction in this.onChangeActions.OnAbortActions)
                {
                    onAbortAction.Invoke(message, ex);
                }
            }
            catch (Exception e)
            {
                this.logger.Error(clientId, "End user on error actions threw an exception. Terminating. ", e);
                throw new TerminateClientException("End user on error actions threw an exception.", e);
            }
        }
        finally
        {
            this.actionsSemaphore.Release();
        }
    }
}
