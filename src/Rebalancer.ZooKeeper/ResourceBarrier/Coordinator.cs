namespace Rebalancer.ZooKeeper.ResourceBarrier;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Logging;
using org.apache.zookeeper;
using ResourceManagement;
using Zk;

public class Coordinator : Watcher, ICoordinator
{
    private readonly string clientId;

    // immutable state
    private readonly CancellationToken coordinatorToken;
    private readonly Stopwatch disconnectedTimer;
    private readonly BlockingCollection<CoordinatorEvent> events;
    private readonly IRebalancerLogger logger;
    private readonly TimeSpan minimumRebalancingInterval;
    private readonly TimeSpan onStartDelay;
    private readonly TimeSpan sessionTimeout;

    private readonly ResourceManager store;

    // services
    private readonly IZooKeeperService zooKeeperService;
    private bool ignoreWatches;
    private RebalancingResult? lastRebalancingResult;
    private CancellationTokenSource rebalancingCts;

    // mutable state
    private Task rebalancingTask;
    private int resourcesVersion;

    public Coordinator(IZooKeeperService zooKeeperService,
        IRebalancerLogger logger,
        ResourceManager store,
        string clientId,
        TimeSpan minimumRebalancingInterval,
        TimeSpan sessionTimeout,
        TimeSpan onStartDelay,
        CancellationToken coordinatorToken)
    {
        this.zooKeeperService = zooKeeperService;
        this.logger = logger;
        this.store = store;
        this.clientId = clientId;
        this.minimumRebalancingInterval = minimumRebalancingInterval;
        this.sessionTimeout = sessionTimeout;
        this.onStartDelay = onStartDelay;
        this.coordinatorToken = coordinatorToken;

        this.rebalancingCts = new CancellationTokenSource();
        this.events = new BlockingCollection<CoordinatorEvent>();
        this.disconnectedTimer = new Stopwatch();
    }

    public async Task<BecomeCoordinatorResult> BecomeCoordinatorAsync(int currentEpoch)
    {
        try
        {
            this.ignoreWatches = false;
            await this.zooKeeperService.IncrementAndWatchEpochAsync(currentEpoch, this);
            await this.zooKeeperService.WatchNodesAsync(this);

            var getResourcesRes = await this.zooKeeperService.GetResourcesAsync(this, null);
            this.resourcesVersion = getResourcesRes.Version;
        }
        catch (ZkStaleVersionException e)
        {
            this.logger.Error(this.clientId, "Could not become coordinator as a stale version number was used", e);
            return BecomeCoordinatorResult.StaleEpoch;
        }
        catch (ZkInvalidOperationException e)
        {
            this.logger.Error(this.clientId, "Could not become coordinator as an invalid ZooKeeper operation occurred",
                e);
            return BecomeCoordinatorResult.Error;
        }

        this.events.Add(CoordinatorEvent.RebalancingTriggered);
        return BecomeCoordinatorResult.Ok;
    }

    public async Task<CoordinatorExitReason> StartEventLoopAsync()
    {
        Stopwatch rebalanceTimer = new();

        while (!this.coordinatorToken.IsCancellationRequested)
        {
            if (this.disconnectedTimer.IsRunning && this.disconnectedTimer.Elapsed > this.sessionTimeout)
            {
                this.zooKeeperService.SessionExpired();
                await this.CleanUpAsync();
                return CoordinatorExitReason.SessionExpired;
            }

            if (this.events.TryTake(out var coordinatorEvent))
            {
                switch (coordinatorEvent)
                {
                    case CoordinatorEvent.SessionExpired:
                        this.zooKeeperService.SessionExpired();
                        await this.CleanUpAsync();
                        return CoordinatorExitReason.SessionExpired;

                    case CoordinatorEvent.NoLongerCoordinator:
                        await this.CleanUpAsync();
                        return CoordinatorExitReason.NoLongerCoordinator;

                    case CoordinatorEvent.PotentialInconsistentState:
                        await this.CleanUpAsync();
                        return CoordinatorExitReason.PotentialInconsistentState;

                    case CoordinatorEvent.FatalError:
                        await this.CleanUpAsync();
                        return CoordinatorExitReason.FatalError;

                    case CoordinatorEvent.RebalancingTriggered:
                        if (this.events.Any())
                        {
                            // skip this event. All other events take precedence over rebalancing
                            // there may be multiple rebalancing events, so if the events collection
                            // consists only of rebalancing events then we'll just process the last one
                        }
                        else if (!rebalanceTimer.IsRunning || rebalanceTimer.Elapsed > this.minimumRebalancingInterval)
                        {
                            await this.CancelRebalancingIfInProgressAsync();
                            rebalanceTimer.Reset();
                            rebalanceTimer.Start();
                            this.logger.Info(this.clientId, "Coordinator - Rebalancing triggered");
                            this.rebalancingTask = Task.Run(async () =>
                                await this.TriggerRebalancing(this.rebalancingCts.Token));
                        }
                        else
                        {
                            // if enough time has not passed since the last rebalancing just readd it
                            this.events.Add(CoordinatorEvent.RebalancingTriggered);
                        }

                        break;
                    default:
                        await this.CleanUpAsync();
                        return CoordinatorExitReason.PotentialInconsistentState;
                }
            }

            await this.WaitFor(TimeSpan.FromSeconds(1));
        }

        if (this.coordinatorToken.IsCancellationRequested)
        {
            await this.CancelRebalancingIfInProgressAsync();
            await this.zooKeeperService.CloseSessionAsync();
            return CoordinatorExitReason.Cancelled;
        }

        return CoordinatorExitReason.PotentialInconsistentState; // if this happens then we have a correctness bug
    }

    // very important that this method does not throw any exceptions as it is called from the zookeeper library
    public override async Task process(WatchedEvent @event)
    {
        if (this.coordinatorToken.IsCancellationRequested || this.ignoreWatches)
        {
            return;
        }

        if (@event.getPath() != null)
        {
            this.logger.Info(this.clientId,
                $"Coordinator - KEEPER EVENT {@event.getState()} - {@event.get_Type()} - {@event.getPath()}");
        }
        else
        {
            this.logger.Info(this.clientId, $"Coordinator - KEEPER EVENT {@event.getState()} - {@event.get_Type()}");
        }

        switch (@event.getState())
        {
            case Event.KeeperState.Expired:
                this.events.Add(CoordinatorEvent.SessionExpired);
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

                if (@event.getPath() != null)
                {
                    if (@event.get_Type() == Event.EventType.NodeDataChanged)
                    {
                        if (@event.getPath().EndsWith("epoch"))
                        {
                            this.events.Add(CoordinatorEvent.NoLongerCoordinator);
                        }
                    }
                    else if (@event.get_Type() == Event.EventType.NodeChildrenChanged)
                    {
                        if (@event.getPath().EndsWith("resources"))
                        {
                            this.events.Add(CoordinatorEvent.RebalancingTriggered);
                        }
                        else if (@event.getPath().EndsWith("clients"))
                        {
                            this.events.Add(CoordinatorEvent.RebalancingTriggered);
                        }
                    }
                }

                break;
            default:
                this.logger.Error(this.clientId,
                    $"Coordinator - Currently this library does not support ZooKeeper state {@event.getState()}");
                this.events.Add(CoordinatorEvent.PotentialInconsistentState);
                break;
        }

        await Task.Yield();
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
            await this.store.InvokeOnStopActionsAsync(this.clientId, "Coordinator");
        }
    }

    private async Task CancelRebalancingIfInProgressAsync()
    {
        if (this.rebalancingTask != null && !this.rebalancingTask.IsCompleted)
        {
            this.logger.Info(this.clientId, "Coordinator - Cancelling the rebalancing that is in progress");
            this.rebalancingCts.Cancel();
            try
            {
                await this.rebalancingTask; // might need to put a time limit on this
            }
            catch (Exception ex)
            {
                this.logger.Error(this.clientId, "Coordinator - Errored on cancelling rebalancing", ex);
                this.events.Add(CoordinatorEvent.PotentialInconsistentState);
            }

            this.rebalancingCts = new CancellationTokenSource(); // reset cts
        }
    }

    private async Task WaitFor(TimeSpan waitPeriod)
    {
        try
        {
            await Task.Delay(waitPeriod, this.coordinatorToken);
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

    private async Task TriggerRebalancing(CancellationToken rebalancingToken)
    {
        try
        {
            await this.zooKeeperService.WatchResourcesChildrenAsync(this);
            await this.zooKeeperService.WatchNodesAsync(this);

            var result = await this.RebalanceAsync(rebalancingToken);
            switch (result)
            {
                case RebalancingResult.Complete:
                    this.logger.Info(this.clientId, "Coordinator - Rebalancing complete");
                    break;
                case RebalancingResult.Cancelled:
                    this.logger.Info(this.clientId, "Coordinator - Rebalancing cancelled");
                    break;
            }

            this.lastRebalancingResult = result;
        }
        catch (ZkSessionExpiredException e)
        {
            this.logger.Error(this.clientId, "Coordinator - The current session has expired", e);
            this.events.Add(CoordinatorEvent.SessionExpired);
            this.lastRebalancingResult = RebalancingResult.Failed;
        }
        catch (ZkStaleVersionException e)
        {
            this.logger.Error(this.clientId,
                "Coordinator - A stale znode version was used, aborting rebalancing.", e);
            this.events.Add(CoordinatorEvent.NoLongerCoordinator);
            this.lastRebalancingResult = RebalancingResult.Failed;
        }
        catch (ZkInvalidOperationException e)
        {
            this.lastRebalancingResult = RebalancingResult.Failed;
            this.logger.Error(this.clientId,
                "Coordinator - An invalid ZooKeeper operation occurred, aborting rebalancing.",
                e);
            this.events.Add(CoordinatorEvent.PotentialInconsistentState);
        }
        catch (InconsistentStateException e)
        {
            this.lastRebalancingResult = RebalancingResult.Failed;
            this.logger.Error(this.clientId,
                "Coordinator - An error occurred potentially leaving the client in an inconsistent state, aborting rebalancing.",
                e);
            this.events.Add(CoordinatorEvent.PotentialInconsistentState);
        }
        catch (TerminateClientException e)
        {
            this.lastRebalancingResult = RebalancingResult.Failed;
            this.logger.Error(this.clientId,
                "Coordinator - A fatal error has occurred, aborting rebalancing.",
                e);
            this.events.Add(CoordinatorEvent.FatalError);
        }
        catch (ZkOperationCancelledException)
        {
            this.logger.Warn(this.clientId, "Coordinator - Rebalancing cancelled");
            this.lastRebalancingResult = RebalancingResult.Cancelled;
        }
        catch (Exception e)
        {
            this.lastRebalancingResult = RebalancingResult.Failed;
            this.logger.Error(this.clientId,
                "Coordinator - An unexpected error has occurred, aborting rebalancing.", e);
            this.events.Add(CoordinatorEvent.PotentialInconsistentState);
        }
    }

    private async Task<RebalancingResult> RebalanceAsync(CancellationToken rebalancingToken)
    {
        Stopwatch sw = new();
        sw.Start();

        this.logger.Info(this.clientId, "Coordinator - Get clients and resources list");
        var clients = await this.zooKeeperService.GetActiveClientsAsync();
        var resources = await this.zooKeeperService.GetResourcesAsync(null, null);

        if (resources.Version != this.resourcesVersion)
        {
            throw new ZkStaleVersionException(
                "Resources znode version does not match expected value, indicates another client has been made coordinator and is executing a rebalancing.");
        }

        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        // if no resources were changed and there are more clients than resources then check
        // to see if rebalancing is necessary. If existing assignments are still valid then
        // a new client or the loss of a client with no assignments need not trigger a rebalancing
        if (!this.IsRebalancingRequired(clients, resources))
        {
            this.logger.Info(this.clientId,
                "Coordinator - No rebalancing required. No resource change. No change to existing clients. More clients than resources.");
            return RebalancingResult.Complete;
        }

        this.logger.Info(this.clientId,
            $"Coordinator - Assign resources ({string.Join(",", resources.Resources)}) to clients ({string.Join(",", clients.ClientPaths.Select(this.GetClientId))})");
        Queue<string> resourcesToAssign = new(resources.Resources);
        List<ResourceAssignment> resourceAssignments = new();
        var clientIndex = 0;
        while (resourcesToAssign.Any())
        {
            resourceAssignments.Add(new ResourceAssignment
            {
                ClientId = this.GetClientId(clients.ClientPaths[clientIndex]),
                Resource = resourcesToAssign.Dequeue()
            });

            clientIndex++;
            if (clientIndex >= clients.ClientPaths.Count)
            {
                clientIndex = 0;
            }
        }

        // write assignments back to resources znode
        resources.ResourceAssignments.Assignments = resourceAssignments;
        this.resourcesVersion = await this.zooKeeperService.SetResourcesAsync(resources);

        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        await this.store.InvokeOnStopActionsAsync(this.clientId, "Coordinator");
        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        if (this.onStartDelay.Ticks > 0)
        {
            this.logger.Info(this.clientId,
                $"Coordinator - Delaying on start for {(int)this.onStartDelay.TotalMilliseconds}ms");
            await this.WaitFor(this.onStartDelay, rebalancingToken);
        }

        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        var leaderAssignments = resourceAssignments
            .Where(x => x.ClientId == this.clientId)
            .Select(x => x.Resource)
            .ToList();
        await this.store.InvokeOnStartActionsAsync(this.clientId, "Coordinator", leaderAssignments, rebalancingToken,
            this.coordinatorToken);
        if (rebalancingToken.IsCancellationRequested)
        {
            return RebalancingResult.Cancelled;
        }

        return RebalancingResult.Complete;
    }

    private bool IsRebalancingRequired(ClientsZnode clients, ResourcesZnode resources)
    {
        // if this is the first rebalancing as coordinator or the last one was not successful then rebalancing is required
        if (this.store.GetAssignmentStatus() == AssignmentStatus.NoAssignmentYet ||
            !this.lastRebalancingResult.HasValue || this.lastRebalancingResult.Value != RebalancingResult.Complete)
        {
            return true;
        }

        // any change to resources requires a rebalancing
        if (resources.HasResourceChange())
        {
            return true;
        }

        // given a client was either added or removed

        // if there are less clients than resources then we require a rebalancing
        if (clients.ClientPaths.Count < resources.Resources.Count)
        {
            return true;
        }

        // given we have an equal or greater number clients than resources

        // if an existing client is currently assigned more than one resource we require a rebalancing
        if (resources.ResourceAssignments.Assignments.GroupBy(x => x.ClientId).Any(x => x.Count() > 1))
        {
            return true;
        }

        // given all existing assignments are one client to one resource

        // if any client for the existing assignments is no longer around then we require a rebalancing
        var clientIds = clients.ClientPaths.Select(this.GetClientId).ToList();
        foreach (var assignment in resources.ResourceAssignments.Assignments)
        {
            if (!clientIds.Contains(assignment.ClientId, StringComparer.Ordinal))
            {
                return true;
            }
        }

        // otherwise no rebalancing is required
        return false;
    }

    private string GetClientId(string clientPath) =>
        clientPath.Substring(clientPath.LastIndexOf("/", StringComparison.Ordinal) + 1);
}
