using org.apache.zookeeper;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper.ResourceManagement;
using Rebalancer.ZooKeeper.Zk;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.ZooKeeper.ResourceBarrier;

public class Follower : Watcher, IFollower
{
    // immutable state
    private readonly string clientId;
    private readonly int clientNumber;
    private readonly Stopwatch disconnectedTimer;
    private readonly BlockingCollection<FollowerEvent> events;
    private readonly CancellationToken followerToken;
    private readonly IRebalancerLogger logger;
    private readonly TimeSpan onStartDelay;
    private readonly TimeSpan sessionTimeout;

    private readonly ResourceManager store;

    // services
    private readonly IZooKeeperService zooKeeperService;
    private bool ignoreWatches;
    private CancellationTokenSource rebalancingCts;
    private Task rebalancingTask;
    private string siblingId;

    // mutable state
    private string watchSiblingPath;

    public Follower(
        IZooKeeperService zooKeeperService,
        IRebalancerLogger logger,
        ResourceManager store,
        string clientId,
        int clientNumber,
        string watchSiblingPath,
        TimeSpan sessionTimeout,
        TimeSpan onStartDelay,
        CancellationToken followerToken)
    {
        this.zooKeeperService = zooKeeperService;
        this.logger = logger;
        this.store = store;
        this.clientId = clientId;
        this.clientNumber = clientNumber;
        this.watchSiblingPath = watchSiblingPath;
        siblingId = watchSiblingPath.Substring(watchSiblingPath.LastIndexOf("/", StringComparison.Ordinal));
        this.sessionTimeout = sessionTimeout;
        this.onStartDelay = onStartDelay;
        this.followerToken = followerToken;

        rebalancingCts = new CancellationTokenSource();
        events = new BlockingCollection<FollowerEvent>();
        disconnectedTimer = new Stopwatch();
    }

    public async Task<BecomeFollowerResult> BecomeFollowerAsync()
    {
        try
        {
            ignoreWatches = false;
            await zooKeeperService.WatchSiblingNodeAsync(watchSiblingPath, this);
            logger.Info(clientId, $"Follower - Set a watch on sibling node {watchSiblingPath}");

            await zooKeeperService.WatchResourcesDataAsync(this);
            logger.Info(clientId, "Follower - Set a watch on resources node");
        }
        catch (ZkNoEphemeralNodeWatchException)
        {
            logger.Info(clientId, "Follower - Could not set a watch on the sibling node as it has gone");
            return BecomeFollowerResult.WatchSiblingGone;
        }
        catch (Exception e)
        {
            logger.Error("Follower - Could not become a follower due to an error", e);
            return BecomeFollowerResult.Error;
        }

        return BecomeFollowerResult.Ok;
    }

    public async Task<FollowerExitReason> StartEventLoopAsync()
    {
        // it is possible that rebalancing has been triggered already, so check 
        // if any resources have been assigned already and if so, add a RebalancingTriggered event
        await CheckForRebalancingAsync();

        while (!followerToken.IsCancellationRequested)
        {
            if (disconnectedTimer.IsRunning && disconnectedTimer.Elapsed > sessionTimeout)
            {
                zooKeeperService.SessionExpired();
                await CleanUpAsync();
                return FollowerExitReason.SessionExpired;
            }

            if (events.TryTake(out var followerEvent))
            {
                switch (followerEvent)
                {
                    case FollowerEvent.SessionExpired:
                        zooKeeperService.SessionExpired();
                        await CleanUpAsync();
                        return FollowerExitReason.SessionExpired;

                    case FollowerEvent.IsNewLeader:
                        await CleanUpAsync();
                        return FollowerExitReason.PossibleRoleChange;

                    case FollowerEvent.PotentialInconsistentState:
                        await CleanUpAsync();
                        return FollowerExitReason.PotentialInconsistentState;

                    case FollowerEvent.FatalError:
                        await CleanUpAsync();
                        return FollowerExitReason.FatalError;

                    case FollowerEvent.RebalancingTriggered:
                        if (events.Any())
                        {
                            // skip this event. All other events take precedence over rebalancing
                            // there may be multiple rebalancing events, so if the events collection
                            // consists only of rebalancing events then we'll just process the last one
                        }
                        else
                        {
                            await CancelRebalancingIfInProgressAsync();
                            logger.Info(clientId, "Follower - Rebalancing triggered");
                            rebalancingTask = Task.Run(async () => await RespondToRebalancing(rebalancingCts.Token));
                        }

                        break;

                    default:
                        await CleanUpAsync();
                        return FollowerExitReason.PotentialInconsistentState;
                }
            }

            await WaitFor(TimeSpan.FromSeconds(1));
        }

        if (followerToken.IsCancellationRequested)
        {
            await CleanUpAsync();
            await zooKeeperService.CloseSessionAsync();
            return FollowerExitReason.Cancelled;
        }

        return FollowerExitReason.PotentialInconsistentState;
    }


    // Important that nothing throws an exception in this method as it is called from the zookeeper library
    public override async Task process(WatchedEvent @event)
    {
        if (followerToken.IsCancellationRequested || ignoreWatches)
        {
            return;
        }

        if (@event.getPath() != null)
        {
            logger.Info(
                clientId,
                $"Follower - KEEPER EVENT {@event.getState()} - {@event.get_Type()} - {@event.getPath()}");
        }
        else
        {
            logger.Info(clientId, $"Follower - KEEPER EVENT {@event.getState()} - {@event.get_Type()}");
        }

        switch (@event.getState())
        {
            case Event.KeeperState.Expired:
                events.Add(FollowerEvent.SessionExpired);
                break;
            case Event.KeeperState.Disconnected:
                if (!disconnectedTimer.IsRunning)
                {
                    disconnectedTimer.Start();
                }

                break;
            case Event.KeeperState.ConnectedReadOnly:
            case Event.KeeperState.SyncConnected:
                if (disconnectedTimer.IsRunning)
                {
                    disconnectedTimer.Reset();
                }

                if (@event.get_Type() == Event.EventType.NodeDeleted)
                {
                    if (@event.getPath().EndsWith(siblingId))
                    {
                        await PerformLeaderCheckAsync();
                    }
                    else
                    {
                        logger.Error(clientId, $"Follower - Unexpected node deletion detected of {@event.getPath()}");
                        events.Add(FollowerEvent.PotentialInconsistentState);
                    }
                }
                else if (@event.get_Type() == Event.EventType.NodeDataChanged)
                {
                    if (@event.getPath().EndsWith("resources"))
                    {
                        await SendTriggerRebalancingEvent();
                    }
                }

                break;
            default:
                logger.Error(
                    clientId,
                    $"Follower - Currently this library does not support ZooKeeper state {@event.getState()}");
                events.Add(FollowerEvent.PotentialInconsistentState);
                break;
        }
    }

    private async Task SendTriggerRebalancingEvent()
    {
        try
        {
            await zooKeeperService.WatchResourcesDataAsync(this);
            events.Add(FollowerEvent.RebalancingTriggered);
        }
        catch (Exception e)
        {
            logger.Error("Could not put a watch on the resources node", e);
            events.Add(FollowerEvent.PotentialInconsistentState);
        }
    }

    private async Task CheckForRebalancingAsync()
    {
        var resources = await zooKeeperService.GetResourcesAsync(null, null);
        var assignedResources = resources.ResourceAssignments.Assignments.Where(x => x.ClientId.Equals(clientId))
            .Select(x => x.Resource)
            .ToList();

        if (assignedResources.Any())
        {
            events.Add(FollowerEvent.RebalancingTriggered);
        }
    }

    private async Task RespondToRebalancing(CancellationToken rebalancingToken)
    {
        try
        {
            var result = await ProcessStatusChangeAsync(rebalancingToken);
            switch (result)
            {
                case RebalancingResult.Complete:
                    logger.Info(clientId, "Follower - Rebalancing complete");
                    break;

                case RebalancingResult.Cancelled:
                    logger.Info(clientId, "Follower - Rebalancing cancelled");
                    break;

                default:
                    logger.Error(clientId, $"Follower - A non-supported RebalancingResult has been returned: {result}");
                    events.Add(FollowerEvent.PotentialInconsistentState);
                    break;
            }
        }
        catch (ZkSessionExpiredException)
        {
            logger.Warn(clientId, "Follower - The session was lost during rebalancing");
            events.Add(FollowerEvent.SessionExpired);
        }
        catch (ZkOperationCancelledException)
        {
            logger.Warn(clientId, "Follower - The rebalancing has been cancelled");
        }
        catch (InconsistentStateException e)
        {
            logger.Error(
                clientId,
                "Follower - An error occurred potentially leaving the client in an inconsistent state. Termination of the client or creationg of a new session will follow",
                e);
            events.Add(FollowerEvent.PotentialInconsistentState);
        }
        catch (TerminateClientException e)
        {
            logger.Error(clientId, "Follower - A fatal error occurred, aborting", e);
            events.Add(FollowerEvent.FatalError);
        }
        catch (Exception e)
        {
            logger.Error(clientId, "Follower - Rebalancing failed.", e);
            events.Add(FollowerEvent.PotentialInconsistentState);
        }
    }

    private async Task<RebalancingResult> ProcessStatusChangeAsync(CancellationToken rebalancingToken)
    {
        await store.InvokeOnStopActionsAsync(clientId, "Follower");

        var resources = await zooKeeperService.GetResourcesAsync(null, null);
        var assignedResources = resources.ResourceAssignments.Assignments.Where(x => x.ClientId.Equals(clientId))
            .Select(x => x.Resource)
            .ToList();

        if (onStartDelay.Ticks > 0)
        {
            logger.Info(clientId, $"Follower - Delaying on start for {(int)onStartDelay.TotalMilliseconds}ms");
            await WaitFor(onStartDelay, rebalancingToken);
        }

        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        await store.InvokeOnStartActionsAsync(clientId, "Follower", assignedResources, rebalancingToken, followerToken);

        return RebalancingResult.Complete;
    }

    private async Task CleanUpAsync()
    {
        try
        {
            ignoreWatches = true;
            await CancelRebalancingIfInProgressAsync();
        }
        finally
        {
            await store.InvokeOnStopActionsAsync(clientId, "Follower");
        }
    }

    private async Task CancelRebalancingIfInProgressAsync()
    {
        if (rebalancingTask != null && !rebalancingTask.IsCompleted)
        {
            logger.Info(clientId, "Follower - Cancelling the rebalancing that is in progress");
            rebalancingCts.Cancel();
            try
            {
                await rebalancingTask; // might need to put a time limit on this
            }
            catch (Exception ex)
            {
                logger.Error(clientId, "Follower - Errored on cancelling rebalancing", ex);
                events.Add(FollowerEvent.PotentialInconsistentState);
            }

            rebalancingCts = new CancellationTokenSource(); // reset cts
        }
    }

    private async Task WaitFor(TimeSpan waitPeriod)
    {
        try
        {
            await Task.Delay(waitPeriod, followerToken);
        }
        catch (TaskCanceledException)
        {
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

    private async Task PerformLeaderCheckAsync()
    {
        var checkComplete = false;
        while (!checkComplete)
        {
            try
            {
                var maxClientNumber = -1;
                var watchChild = string.Empty;
                var clients = await zooKeeperService.GetActiveClientsAsync();

                foreach (var childPath in clients.ClientPaths)
                {
                    var siblingClientNumber = int.Parse(childPath.Substring(childPath.Length - 10, 10));
                    if (siblingClientNumber > maxClientNumber && siblingClientNumber < clientNumber)
                    {
                        watchChild = childPath;
                        maxClientNumber = siblingClientNumber;
                    }
                }

                if (maxClientNumber == -1)
                {
                    events.Add(FollowerEvent.IsNewLeader);
                }
                else
                {
                    watchSiblingPath = watchChild;
                    siblingId = watchSiblingPath.Substring(watchChild.LastIndexOf("/", StringComparison.Ordinal));
                    await zooKeeperService.WatchSiblingNodeAsync(watchChild, this);
                    logger.Info(clientId, $"Follower - Set a watch on sibling node {watchSiblingPath}");
                }

                checkComplete = true;
            }
            catch (ZkNoEphemeralNodeWatchException)
            {
                // do nothing except wait, the next iteration will find
                // another client or it wil detect that it itself is the new leader
                await WaitFor(TimeSpan.FromSeconds(1));
            }
            catch (ZkSessionExpiredException)
            {
                events.Add(FollowerEvent.SessionExpired);
                checkComplete = true;
            }
            catch (Exception ex)
            {
                logger.Error(clientId, "Follower - Failed looking for sibling to watch", ex);
                events.Add(FollowerEvent.PotentialInconsistentState);
                checkComplete = true;
            }
        }
    }
}
