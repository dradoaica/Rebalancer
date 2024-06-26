using org.apache.zookeeper;
using Rebalancer.Core.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.ZooKeeper.Zk;

public interface IZooKeeperService
{
    void SessionExpired();
    Watcher.Event.KeeperState GetKeeperState();
    Task<bool> StartSessionAsync(TimeSpan sessionTimeout, TimeSpan connectTimeout, CancellationToken token);
    Task CloseSessionAsync();

    Task InitializeGlobalBarrierAsync(
        string clientsPath,
        string statusPath,
        string stoppedPath,
        string resourcesPath,
        string epochPath);

    Task InitializeResourceBarrierAsync(string clientsPath, string resourcesPath, string epochPath);

    Task DeleteClientAsync(string clientPath);
    Task<string> CreateClientAsync();
    Task EnsurePathAsync(string znodePath, byte[] bytesToSet = null);
    Task<int> IncrementAndWatchEpochAsync(int currentEpoch, Watcher watcher);
    Task<int> GetEpochAsync();
    Task<ClientsZnode> GetActiveClientsAsync();
    Task<StatusZnode> GetStatusAsync();
    Task<int> SetStatus(StatusZnode statusZnode);
    Task SetFollowerAsStopped(string clientId);
    Task SetFollowerAsStarted(string clientId);
    Task<ResourcesZnode> GetResourcesAsync(Watcher childWatcher, Watcher dataWatcher);
    Task<int> SetResourcesAsync(ResourcesZnode resourcesZnode);
    Task RemoveResourceBarrierAsync(string resourcePath);
    Task TryPutResourceBarrierAsync(string resourcePath, CancellationToken waitToken, IRebalancerLogger logger);
    Task<List<string>> GetStoppedAsync();
    Task<int> WatchEpochAsync(Watcher watcher);
    Task<StatusZnode> WatchStatusAsync(Watcher watcher);
    Task WatchResourcesChildrenAsync(Watcher watcher);
    Task<int> WatchResourcesDataAsync(Watcher watcher);
    Task WatchNodesAsync(Watcher watcher);
    Task WatchSiblingNodeAsync(string siblingPath, Watcher watcher);
}
