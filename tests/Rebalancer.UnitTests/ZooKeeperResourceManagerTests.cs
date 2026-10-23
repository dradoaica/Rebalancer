using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.ResourceManagement;
using Rebalancer.ZooKeeper.Zk;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class ZooKeeperResourceManagerTests
{
    [Fact]
    public async Task RejectedStart_DoesNotBlockLaterStop()
    {
        var manager = new ResourceManager(
            null!,
            new NullRebalancerLogger(),
            new OnChangeActions(),
            RebalancingMode.GlobalBarrier
        );
        await manager.InvokeOnStartActionsAsync("c1", "Follower", [], CancellationToken.None, CancellationToken.None);

        await Assert.ThrowsAsync<InconsistentStateException>(() =>
            manager.InvokeOnStartActionsAsync("c1", "Follower", [], CancellationToken.None, CancellationToken.None)
        );

        var stop = manager.InvokeOnStopActionsAsync("c1", "Follower");
        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(AssignmentStatus.NoAssignmentYet, manager.GetAssignmentStatus());
    }

    // ADR-0027: on an expired session the barrier deletes fail; the resources must still be forgotten, or the next stop,
    // on a new session, deletes barriers that ZooKeeper freed and another client may hold
    [Fact]
    public async Task AStopOnAnExpiredSession_ForgetsItsResources()
    {
        var zooKeeper = new BarrierZooKeeper
        {
            SessionExpired = true,
        };
        var manager = new ResourceManager(
            zooKeeper,
            new NullRebalancerLogger(),
            new OnChangeActions(),
            RebalancingMode.ResourceBarrier
        );
        await manager.InvokeOnStartActionsAsync(
            "c1",
            "Follower",
            ["r1",],
            CancellationToken.None,
            CancellationToken.None
        );
        Assert.Equal(AssignmentStatus.ResourcesAssigned, manager.GetAssignmentStatus());

        await Assert.ThrowsAsync<ZkSessionExpiredException>(() => manager.InvokeOnStopActionsAsync("c1", "Follower"));
        Assert.Equal(AssignmentStatus.NoAssignmentYet, manager.GetAssignmentStatus());

        zooKeeper.SessionExpired = false; // a new session
        await manager.InvokeOnStopActionsAsync("c2", "Follower");
        Assert.Equal(["r1",], zooKeeper.Removals); // only the failed attempt on the expired session
    }

    // ADR-0028: a disconnection stops the handlers at once; a coordinator restarts them when the session survived
    [Fact]
    public async Task ADisconnection_StopsTheHandlers_AndACoordinatorRestartsThemOnReconnect()
    {
        var (manager, events) = Recording(new BarrierZooKeeper());
        await manager.InvokeOnStartActionsAsync(
            "c1",
            "Coordinator",
            ["r1",],
            CancellationToken.None,
            CancellationToken.None
        );

        manager.Suspend("c1", "Coordinator");
        await WaitUntilAsync(() => events.Contains("stop"));
        Assert.Equal(AssignmentStatus.NoAssignmentYet, manager.GetResources().AssignmentStatus);
        Assert.Empty(manager.GetResources().Resources);

        manager.Resume();
        await manager.RestartAfterResumeAsync("c1", "Coordinator");
        Assert.Equal(["start:r1", "stop", "start:r1",], events);
        Assert.Equal(["r1",], manager.GetResources().Resources);
    }

    // ADR-0028: no handler starts while disconnected; the response after the reconnect starts it, with no extra stop
    [Fact]
    public async Task NoHandlerStartsWhileDisconnected()
    {
        var (manager, events) = Recording(new BarrierZooKeeper());
        manager.Suspend("c1", "Follower");
        await manager.InvokeOnStartActionsAsync(
            "c1",
            "Follower",
            ["r1",],
            CancellationToken.None,
            CancellationToken.None
        );
        Assert.Empty(events);
        Assert.Empty(manager.GetResources().Resources);

        manager.Resume();
        await manager.InvokeOnStopActionsAsync("c1", "Follower");
        await manager.InvokeOnStartActionsAsync(
            "c1",
            "Follower",
            ["r1",],
            CancellationToken.None,
            CancellationToken.None
        );
        Assert.Equal(["start:r1",], events);
    }

    // ADR-0028: a suspension that ends before its stop can run (a start held the actions) stops nothing
    [Fact]
    public async Task ASuspensionThatEndsBeforeItsStopRuns_StopsNothing()
    {
        var zooKeeper = new BarrierZooKeeper
        {
            PutGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var (manager, events) = Recording(zooKeeper);
        var start = manager.InvokeOnStartActionsAsync(
            "c1",
            "Follower",
            ["r1",],
            CancellationToken.None,
            CancellationToken.None
        );

        manager.Suspend("c1", "Follower"); // its stop waits for the start in progress
        manager.Resume();
        zooKeeper.PutGate.SetResult();
        await start;
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // let the stale stop run

        Assert.Equal(["start:r1",], events);
        Assert.Equal(["r1",], manager.GetResources().Resources);
    }

    private static (ResourceManager Manager, List<string> Events) Recording(IZooKeeperService zooKeeper)
    {
        var events = new List<string>();
        var actions = new OnChangeActions();
        actions.AddOnStartAction(resources => events.Add($"start:{string.Join(",", resources)}"));
        actions.AddOnStopAction(() => events.Add("stop"));
        return (new ResourceManager(zooKeeper, new NullRebalancerLogger(), actions, RebalancingMode.ResourceBarrier),
            events);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 5 s");
            await Task.Delay(10);
        }
    }

    /// <summary>Barriers always granted; removals recorded, and failing while the session is expired.</summary>
    private sealed class BarrierZooKeeper : IZooKeeperService
    {
        public bool SessionExpired { get; set; }
        public List<string> Removals { get; } = [];

        // when set, a barrier put waits for it, holding the resource manager's actions
        public TaskCompletionSource? PutGate { get; set; }

        public Task RemoveResourceBarrierAsync(string resourcePath)
        {
            Removals.Add(resourcePath);
            return SessionExpired ? Task.FromException(new ZkSessionExpiredException("session expired"))
                : Task.CompletedTask;
        }

        public Task TryPutResourceBarrierAsync(
            string resourcePath,
            CancellationToken waitToken,
            IRebalancerLogger logger
        ) =>
            PutGate?.Task ?? Task.CompletedTask;

        void IZooKeeperService.SessionExpired() => throw new NotSupportedException();
        public Watcher.Event.KeeperState GetKeeperState() => throw new NotSupportedException();

        public Task<bool> StartSessionAsync(
            TimeSpan sessionTimeout,
            TimeSpan connectTimeout,
            CancellationToken token
        ) =>
            throw new NotSupportedException();

        public Task CloseSessionAsync() => throw new NotSupportedException();

        public Task InitializeGlobalBarrierAsync(
            string clientsPath,
            string statusPath,
            string stoppedPath,
            string resourcesPath,
            string epochPath
        ) => throw new NotSupportedException();

        public Task InitializeResourceBarrierAsync(string clientsPath, string resourcesPath, string epochPath) =>
            throw new NotSupportedException();

        public Task DeleteClientAsync(string clientPath) => throw new NotSupportedException();
        public Task<string> CreateClientAsync() => throw new NotSupportedException();
        public Task EnsurePathAsync(string znodePath, byte[]? bytesToSet = null) => throw new NotSupportedException();

        public Task<int> IncrementAndWatchEpochAsync(int currentEpoch, Watcher watcher) =>
            throw new NotSupportedException();

        public Task<int> GetEpochAsync() => throw new NotSupportedException();
        public Task<ClientsZnode> GetActiveClientsAsync() => throw new NotSupportedException();
        public Task<StatusZnode> GetStatusAsync() => throw new NotSupportedException();
        public Task<int> SetStatus(StatusZnode statusZnode) => throw new NotSupportedException();
        public Task SetFollowerAsStopped(string clientId) => throw new NotSupportedException();
        public Task SetFollowerAsStarted(string clientId) => throw new NotSupportedException();

        public Task<ResourcesZnode> GetResourcesAsync(Watcher childWatcher, Watcher dataWatcher) =>
            throw new NotSupportedException();

        public Task<int> SetResourcesAsync(ResourcesZnode resourcesZnode) => throw new NotSupportedException();
        public Task<List<string>> GetStoppedAsync() => throw new NotSupportedException();
        public Task<int> WatchEpochAsync(Watcher watcher) => throw new NotSupportedException();
        public Task<StatusZnode> WatchStatusAsync(Watcher watcher) => throw new NotSupportedException();
        public Task WatchResourcesChildrenAsync(Watcher watcher) => throw new NotSupportedException();
        public Task<int> WatchResourcesDataAsync(Watcher watcher) => throw new NotSupportedException();
        public Task WatchNodesAsync(Watcher watcher) => throw new NotSupportedException();
        public Task WatchSiblingNodeAsync(string siblingPath, Watcher watcher) => throw new NotSupportedException();
    }
}
