namespace Rebalancer.ZooKeeper.ResourceBarrier;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Logging;
using org.apache.zookeeper;
using ResourceManagement;
using Zk;

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

    public Follower(IZooKeeperService zooKeeperService,
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
        this.siblingId = watchSiblingPath.Substring(watchSiblingPath.LastIndexOf("/", StringComparison.Ordinal));
        this.sessionTimeout = sessionTimeout;
        this.onStartDelay = onStartDelay;
        this.followerToken = followerToken;

        this.rebalancingCts = new CancellationTokenSource();
        this.events = new BlockingCollection<FollowerEvent>();
        this.disconnectedTimer = new Stopwatch();
    }

    public async Task<BecomeFollowerResult> BecomeFollowerAsync()
    {
        try
        {
            this.ignoreWatches = false;
            await this.zooKeeperService.WatchSiblingNodeAsync(this.watchSiblingPath, this);
            this.logger.Info(this.clientId, $"Follower - Set a watch on sibling node {this.watchSiblingPath}");

            await this.zooKeeperService.WatchResourcesDataAsync(this);
            this.logger.Info(this.clientId, "Follower - Set a watch on resources node");
        }
        catch (ZkNoEphemeralNodeWatchException)
        {
            this.logger.Info(this.clientId, "Follower - Could not set a watch on the sibling node as it has gone");
            return BecomeFollowerResult.WatchSiblingGone;
        }
        catch (Exception e)
        {
            this.logger.Error("Follower - Could not become a follower due to an error", e);
            return BecomeFollowerResult.Error;
        }

        return BecomeFollowerResult.Ok;
    }

    public async Task<FollowerExitReason> StartEventLoopAsync()
    {
        // it is possible that rebalancing has been triggered already, so check 
        // if any resources have been assigned already and if so, add a RebalancingTriggered event
        await this.CheckForRebalancingAsync();

        while (!this.followerToken.IsCancellationRequested)
        {
            if (this.disconnectedTimer.IsRunning && this.disconnectedTimer.Elapsed > this.sessionTimeout)
            {
                this.zooKeeperService.SessionExpired();
                await this.CleanUpAsync();
                return FollowerExitReason.SessionExpired;
            }

            if (this.events.TryTake(out var followerEvent))
            {
                switch (followerEvent)
                {
                    case FollowerEvent.SessionExpired:
                        this.zooKeeperService.SessionExpired();
                        await this.CleanUpAsync();
                        return FollowerExitReason.SessionExpired;

                    case FollowerEvent.IsNewLeader:
                        await this.CleanUpAsync();
                        return FollowerExitReason.PossibleRoleChange;

                    case FollowerEvent.PotentialInconsistentState:
                        await this.CleanUpAsync();
                        return FollowerExitReason.PotentialInconsistentState;

                    case FollowerEvent.FatalError:
                        await this.CleanUpAsync();
                        return FollowerExitReason.FatalError;

                    case FollowerEvent.RebalancingTriggered:
                        if (this.events.Any())
                        {
                            // skip this event. All other events take precedence over rebalancing
                            // there may be multiple rebalancing events, so if the events collection
                            // consists only of rebalancing events then we'll just process the last one
                        }
                        else
                        {
                            await this.CancelRebalancingIfInProgressAsync();
                            this.logger.Info(this.clientId, "Follower - Rebalancing triggered");
                            this.rebalancingTask = Task.Run(async () =>
                                await this.RespondToRebalancing(this.rebalancingCts.Token));
                        }

                        break;

                    default:
                        await this.CleanUpAsync();
                        return FollowerExitReason.PotentialInconsistentState;
                }
            }

            await this.WaitFor(TimeSpan.FromSeconds(1));
        }

        if (this.followerToken.IsCancellationRequested)
        {
            await this.CleanUpAsync();
            await this.zooKeeperService.CloseSessionAsync();
            return FollowerExitReason.Cancelled;
        }

        return FollowerExitReason.PotentialInconsistentState;
    }


    // Important that nothing throws an exception in this method as it is called from the zookeeper library
    public override async Task process(WatchedEvent @event)
    {
        if (this.followerToken.IsCancellationRequested || this.ignoreWatches)
        {
            return;
        }

        if (@event.getPath() != null)
        {
            this.logger.Info(this.clientId,
                $"Follower - KEEPER EVENT {@event.getState()} - {@event.get_Type()} - {@event.getPath()}");
        }
        else
        {
            this.logger.Info(this.clientId, $"Follower - KEEPER EVENT {@event.getState()} - {@event.get_Type()}");
        }

        switch (@event.getState())
        {
            case Event.KeeperState.Expired:
                this.events.Add(FollowerEvent.SessionExpired);
                break;
            case Event.KeeperState.Disconnected:
                if (!this.disconnectedTimer.IsRunning)
                {
                    this.disconnectedTimer.Start();
                }

                break;
            case Event.KeeperState.ConnectedReadOnly:
            case Event.KeeperState.SyncConnected:
                if (this.disconnectedTimer.IsRunning)
                {
                    this.disconnectedTimer.Reset();
                }

                if (@event.get_Type() == Event.EventType.NodeDeleted)
                {
                    if (@event.getPath().EndsWith(this.siblingId))
                    {
                        await this.PerformLeaderCheckAsync();
                    }
                    else
                    {
                        this.logger.Error(this.clientId,
                            $"Follower - Unexpected node deletion detected of {@event.getPath()}");
                        this.events.Add(FollowerEvent.PotentialInconsistentState);
                    }
                }
                else if (@event.get_Type() == Event.EventType.NodeDataChanged)
                {
                    if (@event.getPath().EndsWith("resources"))
                    {
                        await this.SendTriggerRebalancingEvent();
                    }
                }

                break;
            default:
                this.logger.Error(this.clientId,
                    $"Follower - Currently this library does not support ZooKeeper state {@event.getState()}");
                this.events.Add(FollowerEvent.PotentialInconsistentState);
                break;
        }
    }

    private async Task SendTriggerRebalancingEvent()
    {
        try
        {
            await this.zooKeeperService.WatchResourcesDataAsync(this);
            this.events.Add(FollowerEvent.RebalancingTriggered);
        }
        catch (Exception e)
        {
            this.logger.Error("Could not put a watch on the resources node", e);
            this.events.Add(FollowerEvent.PotentialInconsistentState);
        }
    }

    private async Task CheckForRebalancingAsync()
    {
        var resources = await this.zooKeeperService.GetResourcesAsync(null, null);
        var assignedResources = resources.ResourceAssignments.Assignments
            .Where(x => x.ClientId.Equals(this.clientId))
            .Select(x => x.Resource)
            .ToList();

        if (assignedResources.Any())
        {
            this.events.Add(FollowerEvent.RebalancingTriggered);
        }
    }

    private async Task RespondToRebalancing(CancellationToken rebalancingToken)
    {
        try
        {
            var result = await this.ProcessStatusChangeAsync(rebalancingToken);
            switch (result)
            {
                case RebalancingResult.Complete:
                    this.logger.Info(this.clientId, "Follower - Rebalancing complete");
                    break;

                case RebalancingResult.Cancelled:
                    this.logger.Info(this.clientId, "Follower - Rebalancing cancelled");
                    break;

                default:
                    this.logger.Error(this.clientId,
                        $"Follower - A non-supported RebalancingResult has been returned: {result}");
                    this.events.Add(FollowerEvent.PotentialInconsistentState);
                    break;
            }
        }
        catch (ZkSessionExpiredException)
        {
            this.logger.Warn(this.clientId, "Follower - The session was lost during rebalancing");
            this.events.Add(FollowerEvent.SessionExpired);
        }
        catch (ZkOperationCancelledException)
        {
            this.logger.Warn(this.clientId, "Follower - The rebalancing has been cancelled");
        }
        catch (InconsistentStateException e)
        {
            this.logger.Error(this.clientId,
                "Follower - An error occurred potentially leaving the client in an inconsistent state. Termination of the client or creationg of a new session will follow",
                e);
            this.events.Add(FollowerEvent.PotentialInconsistentState);
        }
        catch (TerminateClientException e)
        {
            this.logger.Error(this.clientId, "Follower - A fatal error occurred, aborting", e);
            this.events.Add(FollowerEvent.FatalError);
        }
        catch (Exception e)
        {
            this.logger.Error(this.clientId, "Follower - Rebalancing failed.", e);
            this.events.Add(FollowerEvent.PotentialInconsistentState);
        }
    }

    private async Task<RebalancingResult> ProcessStatusChangeAsync(CancellationToken rebalancingToken)
    {
        await this.store.InvokeOnStopActionsAsync(this.clientId, "Follower");

        var resources = await this.zooKeeperService.GetResourcesAsync(null, null);
        var assignedResources = resources.ResourceAssignments.Assignments
            .Where(x => x.ClientId.Equals(this.clientId))
            .Select(x => x.Resource)
            .ToList();

        if (this.onStartDelay.Ticks > 0)
        {
            this.logger.Info(this.clientId,
                $"Follower - Delaying on start for {(int)this.onStartDelay.TotalMilliseconds}ms");
            await this.WaitFor(this.onStartDelay, rebalancingToken);
        }

        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        await this.store.InvokeOnStartActionsAsync(this.clientId, "Follower", assignedResources, rebalancingToken,
            this.followerToken);

        return RebalancingResult.Complete;
    }

    private async Task CleanUpAsync()
    {
        try
        {
            this.ignoreWatches = true;
            await this.CancelRebalancingIfInProgressAsync();
        }
        finally
        {
            await this.store.InvokeOnStopActionsAsync(this.clientId, "Follower");
        }
    }

    private async Task CancelRebalancingIfInProgressAsync()
    {
        if (this.rebalancingTask != null && !this.rebalancingTask.IsCompleted)
        {
            this.logger.Info(this.clientId, "Follower - Cancelling the rebalancing that is in progress");
            this.rebalancingCts.Cancel();
            try
            {
                await this.rebalancingTask; // might need to put a time limit on this
            }
            catch (Exception ex)
            {
                this.logger.Error(this.clientId, "Follower - Errored on cancelling rebalancing", ex);
                this.events.Add(FollowerEvent.PotentialInconsistentState);
            }

            this.rebalancingCts = new CancellationTokenSource(); // reset cts
        }
    }

    private async Task WaitFor(TimeSpan waitPeriod)
    {
        try
        {
            await Task.Delay(waitPeriod, this.followerToken);
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
                var clients = await this.zooKeeperService.GetActiveClientsAsync();

                foreach (var childPath in clients.ClientPaths)
                {
                    var siblingClientNumber = int.Parse(childPath.Substring(childPath.Length - 10, 10));
                    if (siblingClientNumber > maxClientNumber && siblingClientNumber < this.clientNumber)
                    {
                        watchChild = childPath;
                        maxClientNumber = siblingClientNumber;
                    }
                }

                if (maxClientNumber == -1)
                {
                    this.events.Add(FollowerEvent.IsNewLeader);
                }
                else
                {
                    this.watchSiblingPath = watchChild;
                    this.siblingId =
                        this.watchSiblingPath.Substring(watchChild.LastIndexOf("/", StringComparison.Ordinal));
                    await this.zooKeeperService.WatchSiblingNodeAsync(watchChild, this);
                    this.logger.Info(this.clientId, $"Follower - Set a watch on sibling node {this.watchSiblingPath}");
                }

                checkComplete = true;
            }
            catch (ZkNoEphemeralNodeWatchException)
            {
                // do nothing except wait, the next iteration will find
                // another client or it wil detect that it itself is the new leader
                await this.WaitFor(TimeSpan.FromSeconds(1));
            }
            catch (ZkSessionExpiredException)
            {
                this.events.Add(FollowerEvent.SessionExpired);
                checkComplete = true;
            }
            catch (Exception ex)
            {
                this.logger.Error(this.clientId, "Follower - Failed looking for sibling to watch", ex);
                this.events.Add(FollowerEvent.PotentialInconsistentState);
                checkComplete = true;
            }
        }
    }
}
