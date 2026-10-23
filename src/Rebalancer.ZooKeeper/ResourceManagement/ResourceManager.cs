using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper.Zk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.ZooKeeper.ResourceManagement;

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

    // ADR-0028: whether the start actions ran and the stop actions did not run since
    private bool handlersRunning;

    // mutable statwe
    private List<string> resources;

    // ADR-0028: set while disconnected; the generation tells a stale suspension from the current one
    private bool suspended;
    private int suspensionGeneration;

    public ResourceManager(
        IZooKeeperService zooKeeperService,
        IRebalancerLogger logger,
        OnChangeActions onChangeActions,
        RebalancingMode rebalancingMode
    )
    {
        this.zooKeeperService = zooKeeperService;
        this.logger = logger;
        resources = new List<string>();
        assignmentStatus = AssignmentStatus.NoAssignmentYet;
        this.onChangeActions = onChangeActions;
        this.rebalancingMode = rebalancingMode;
    }

    public AssignmentStatus GetAssignmentStatus()
    {
        lock (resourcesLockObj)
        {
            return assignmentStatus;
        }
    }

    /// <summary>
    ///     The resources whose handlers run. While suspended (ADR-0028) the handlers are stopped, so none are reported
    ///     and the client waits for its assignment again.
    /// </summary>
    public GetResourcesResponse GetResources()
    {
        lock (resourcesLockObj)
        {
            if (assignmentStatus == AssignmentStatus.ResourcesAssigned && suspended && !handlersRunning)
            {
                return new GetResourcesResponse
                {
                    Resources = new List<string>(),
                    AssignmentStatus = AssignmentStatus.NoAssignmentYet,
                };
            }

            if (assignmentStatus == AssignmentStatus.ResourcesAssigned)
            {
                return new GetResourcesResponse
                {
                    Resources = new List<string>(resources),
                    AssignmentStatus = assignmentStatus,
                };
            }

            return new GetResourcesResponse
            {
                Resources = new List<string>(),
                AssignmentStatus = assignmentStatus,
            };
        }
    }

    private void SetResources(AssignmentStatus newAssignmentStatus, List<string> newResources)
    {
        lock (resourcesLockObj)
        {
            assignmentStatus = newAssignmentStatus;
            resources = new List<string>(newResources);
        }
    }

    // the assigned resources, whether or not their handlers run (barriers are held for them)
    private List<string> GetAssignedResources()
    {
        lock (resourcesLockObj)
        {
            return assignmentStatus == AssignmentStatus.ResourcesAssigned ? new List<string>(resources)
                : new List<string>();
        }
    }

    private bool IsSuspended()
    {
        lock (resourcesLockObj)
        {
            return suspended;
        }
    }

    /// <summary>
    ///     ADR-0028: the connection to the ensemble is lost. The stop actions run as soon as no start or stop is in
    ///     progress, unless the connection comes back first; barriers and assigned resources are kept, and no start actions
    ///     run until <see cref="Resume" />. Does not wait, so it can be called from a ZooKeeper watcher.
    /// </summary>
    internal void Suspend(string clientId, string role)
    {
        int generation;
        lock (resourcesLockObj)
        {
            suspended = true;
            generation = ++suspensionGeneration;
        }

        logger.Info(clientId, $"{role} - Disconnected: stopping resource handlers until the connection is back");
        _ = Task.Run(() => StopSuspendedHandlersAsync(clientId, role, generation));
    }

    /// <summary>ADR-0028: the connection is back with the same session, or a new session starts.</summary>
    internal void Resume()
    {
        lock (resourcesLockObj)
        {
            suspended = false;
            suspensionGeneration++;
        }
    }

    /// <summary>
    ///     ADR-0028, for a coordinator after <see cref="Resume" />: restarts the handlers of its own share, which it
    ///     still holds (the session survived). A start already in progress makes this a no-op.
    /// </summary>
    internal async Task RestartAfterResumeAsync(string clientId, string role)
    {
        await actionsSemaphore.WaitAsync();
        try
        {
            var assigned = GetAssignedResources();
            if (IsSuspended() || handlersRunning || !assigned.Any())
            {
                return;
            }

            logger.Info(clientId, $"{role} - Connection back: restarting resource handlers");
            InvokeStartActions(clientId, role, assigned);
        }
        finally
        {
            actionsSemaphore.Release();
        }
    }

    private async Task StopSuspendedHandlersAsync(string clientId, string role, int generation)
    {
        await actionsSemaphore.WaitAsync();
        try
        {
            lock (resourcesLockObj)
            {
                if (generation != suspensionGeneration || !handlersRunning)
                {
                    return;
                }
            }

            InvokeStopActions(clientId, role);
        }
        catch (TerminateClientException)
        {
            // logged by InvokeStopActions; the role's own stop path reports the failure again
        }
        finally
        {
            actionsSemaphore.Release();
        }
    }

    private void InvokeStartActions(string clientId, string role, List<string> startResources)
    {
        try
        {
            logger.Info(clientId, $"{role} - Invoking on start with resources {string.Join(",", startResources)}");
            foreach (var onStartAction in onChangeActions.OnStartActions)
            {
                onStartAction.Invoke(startResources);
            }

            handlersRunning = true;
        }
        catch (Exception e)
        {
            logger.Error(clientId, $"{role} - End user on start actions threw an exception. Terminating. ", e);
            throw new TerminateClientException("End user on start actions threw an exception.", e);
        }
    }

    private void InvokeStopActions(string clientId, string role)
    {
        handlersRunning = false;
        try
        {
            foreach (var onStopAction in onChangeActions.OnStopActions)
            {
                onStopAction.Invoke();
            }
        }
        catch (Exception e)
        {
            logger.Error(clientId, $"{role} - End user on stop actions threw an exception. Terminating. ", e);
            throw new TerminateClientException("End user on stop actions threw an exception.", e);
        }
    }

    public bool IsInStartedState()
    {
        lock (resourcesLockObj)
        {
            return assignmentStatus == AssignmentStatus.ResourcesAssigned ||
                   assignmentStatus == AssignmentStatus.NoResourcesAssigned;
        }
    }

    public async Task InvokeOnStopActionsAsync(string clientId, string role)
    {
        await actionsSemaphore.WaitAsync();

        try
        {
            var resourcesToRemove = GetAssignedResources();
            if (resourcesToRemove.Any())
            {
                logger.Info(
                    clientId,
                    $"{role} - Invoking on stop actions. Unassigned resources {string.Join(",", resourcesToRemove)}"
                );

                // ADR-0028: a suspension may have stopped the handlers already
                if (handlersRunning)
                {
                    InvokeStopActions(clientId, role);
                }

                if (rebalancingMode == RebalancingMode.ResourceBarrier)
                {
                    try
                    {
                        logger.Info(
                            clientId,
                            $"{role} - Removing barriers on resources {string.Join(",", resourcesToRemove)}"
                        );
                        var counter = 1;
                        foreach (var resource in resourcesToRemove)
                        {
                            await zooKeeperService.RemoveResourceBarrierAsync(resource);

                            if (counter % 10 == 0)
                            {
                                logger.Info(
                                    clientId,
                                    $"{role} - Removed barriers on {counter} resources of {resourcesToRemove.Count}"
                                );
                            }

                            counter++;
                        }

                        logger.Info(
                            clientId,
                            $"{role} - Removed barriers on {resourcesToRemove.Count} resources of {resourcesToRemove.Count}"
                        );
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
                        throw new InconsistentStateException("An error occurred while removing resource barriers", e);
                    }
                }

                logger.Info(clientId, $"{role} - On stop complete");
            }
        }
        finally
        {
            // ADR-0027: forget the resources even when removing their barriers failed (an expired session), so a later
            // stop, on a new session, does not act on barriers this client no longer holds
            SetResources(AssignmentStatus.NoAssignmentYet, new List<string>());
            actionsSemaphore.Release();
        }
    }

    public async Task InvokeOnStartActionsAsync(
        string clientId,
        string role,
        List<string> newResources,
        CancellationToken rebalancingToken,
        CancellationToken clientToken
    )
    {
        await actionsSemaphore.WaitAsync();

        try
        {
            if (IsInStartedState())
            {
                throw new InconsistentStateException(
                    "An attempt to invoke on start actions occurred while already in the started state"
                );
            }

            if (newResources.Any())
            {
                if (rebalancingMode == RebalancingMode.ResourceBarrier)
                {
                    try
                    {
                        logger.Info(
                            clientId,
                            $"{role} - Putting barriers on resources {string.Join(",", newResources)}"
                        );
                        var counter = 1;
                        foreach (var resource in newResources)
                        {
                            await zooKeeperService.TryPutResourceBarrierAsync(resource, rebalancingToken, logger);

                            if (counter % 10 == 0)
                            {
                                logger.Info(
                                    clientId,
                                    $"{role} - Put barriers on {counter} resources of {newResources.Count}"
                                );
                            }

                            counter++;
                        }

                        logger.Info(
                            clientId,
                            $"{role} - Put barriers on {newResources.Count} resources of {newResources.Count}"
                        );
                    }
                    catch (ZkOperationCancelledException)
                    {
                        if (clientToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        logger.Info(
                            clientId,
                            $"{role} - Rebalancing cancelled, removing barriers on resources {string.Join(",", newResources)}"
                        );
                        try
                        {
                            var counter = 1;
                            foreach (var resource in newResources)
                            {
                                await zooKeeperService.RemoveResourceBarrierAsync(resource);

                                if (counter % 10 == 0)
                                {
                                    logger.Info(
                                        clientId,
                                        $"{role} - Removing barriers on {counter} resources of {newResources.Count}"
                                    );
                                }

                                counter++;
                            }

                            logger.Info(
                                clientId,
                                $"{role} - Removed barriers on {newResources.Count} resources of {newResources.Count}"
                            );
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
                                e
                            );
                        }

                        return;
                    }
                    catch (ZkSessionExpiredException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        throw new InconsistentStateException("An error occurred while putting resource barriers", e);
                    }
                }

                SetResources(AssignmentStatus.ResourcesAssigned, newResources);

                // ADR-0028: no handler starts while disconnected; the reconnect restarts them
                if (IsSuspended())
                {
                    logger.Info(clientId, $"{role} - Disconnected: on start postponed until the connection is back");
                }
                else
                {
                    InvokeStartActions(clientId, role, newResources);
                }

                logger.Info(clientId, $"{role} - On start complete");
            }
            else
            {
                SetResources(AssignmentStatus.NoResourcesAssigned, newResources);
            }
        }
        finally
        {
            actionsSemaphore.Release();
        }
    }


    public async Task InvokeOnAbortActionsAsync(string clientId, string message, Exception ex = null)
    {
        await actionsSemaphore.WaitAsync();
        try
        {
            logger.Info(clientId, "Invoking on abort actions.");

            try
            {
                foreach (var onAbortAction in onChangeActions.OnAbortActions)
                {
                    onAbortAction.Invoke(message, ex);
                }
            }
            catch (Exception e)
            {
                logger.Error(clientId, "End user on error actions threw an exception. Terminating. ", e);
                throw new TerminateClientException("End user on error actions threw an exception.", e);
            }
        }
        finally
        {
            actionsSemaphore.Release();
        }
    }
}
