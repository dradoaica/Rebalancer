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

    /// <summary>Barriers always granted; removals recorded, and failing while the session is expired.</summary>
    private sealed class BarrierZooKeeper : IZooKeeperService
    {
        public bool SessionExpired { get; set; }
        public List<string> Removals { get; } = [];

        public Task RemoveResourceBarrierAsync(string resourcePath)
        {
            Removals.Add(resourcePath);
            return SessionExpired
                ? Task.FromException(new ZkSessionExpiredException("session expired"))
                : Task.CompletedTask;
        }

        public Task TryPutResourceBarrierAsync(string resourcePath, CancellationToken waitToken, IRebalancerLogger logger) =>
            Task.CompletedTask;

        void IZooKeeperService.SessionExpired() => throw new NotSupportedException();
        public Watcher.Event.KeeperState GetKeeperState() => throw new NotSupportedException();

        public Task<bool> StartSessionAsync(TimeSpan sessionTimeout, TimeSpan connectTimeout, CancellationToken token) =>
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
        public Task<int> IncrementAndWatchEpochAsync(int currentEpoch, Watcher watcher) => throw new NotSupportedException();
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
