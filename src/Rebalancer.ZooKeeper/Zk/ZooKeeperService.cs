using org.apache.zookeeper;
using Rebalancer.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.ZooKeeper.Zk;

public class ZooKeeperService : Watcher, IZooKeeperService
{
    private readonly string zookeeperHosts;
    private string clientId;
    private string clientsPath;
    private string epochPath;
    private Event.KeeperState keeperState;
    private string resourcesPath;
    private bool sessionExpired;
    private string statusPath;
    private string stoppedPath;
    private CancellationToken token;
    private org.apache.zookeeper.ZooKeeper zookeeper;

    public ZooKeeperService(string zookeeperHosts)
    {
        this.zookeeperHosts = zookeeperHosts;
        clientId = "-";
        sessionExpired = false;
    }

    public void SessionExpired() => sessionExpired = true;

    public async Task InitializeGlobalBarrierAsync(
        string clientsPath,
        string statusPath,
        string stoppedPath,
        string resourcesPath,
        string epochPath)
    {
        this.clientsPath = clientsPath;
        this.statusPath = statusPath;
        this.stoppedPath = stoppedPath;
        this.resourcesPath = resourcesPath;
        this.epochPath = epochPath;

        await EnsurePathAsync(this.clientsPath);
        await EnsurePathAsync(this.epochPath);
        await EnsurePathAsync(this.statusPath, BitConverter.GetBytes(0));
        await EnsurePathAsync(this.stoppedPath);
        await EnsurePathAsync(
            this.resourcesPath,
            Encoding.UTF8.GetBytes(JSONSerializer<ResourcesZnodeData>.Serialize(new ResourcesZnodeData())));
    }

    public async Task InitializeResourceBarrierAsync(string clientsPath, string resourcesPath, string epochPath)
    {
        this.clientsPath = clientsPath;
        this.resourcesPath = resourcesPath;
        this.epochPath = epochPath;

        await EnsurePathAsync(this.clientsPath);
        await EnsurePathAsync(this.epochPath);
        await EnsurePathAsync(
            this.resourcesPath,
            Encoding.UTF8.GetBytes(JSONSerializer<ResourcesZnodeData>.Serialize(new ResourcesZnodeData())));
    }

    public Event.KeeperState GetKeeperState() => keeperState;

    public async Task<bool> StartSessionAsync(TimeSpan sessionTimeout, TimeSpan connectTimeout, CancellationToken token)
    {
        this.token = token;
        Stopwatch sw = new();
        sw.Start();

        if (zookeeper != null)
        {
            await zookeeper.closeAsync();
        }

        zookeeper = new org.apache.zookeeper.ZooKeeper(zookeeperHosts, (int)sessionTimeout.TotalMilliseconds, this);

        while (keeperState != Event.KeeperState.SyncConnected && sw.Elapsed <= connectTimeout)
        {
            await Task.Delay(50);
        }

        var connected = keeperState == Event.KeeperState.SyncConnected;
        sessionExpired = !connected;

        return connected;
    }

    public async Task CloseSessionAsync()
    {
        if (zookeeper != null)
        {
            await zookeeper.closeAsync();
        }

        zookeeper = null;
    }

    public async Task<string> CreateClientAsync()
    {
        var actionToPerform = "create client znode";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var clientPath = await zookeeper.createAsync(
                    $"{clientsPath}/c_",
                    Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL_SEQUENTIAL);

                clientId = clientPath.Substring(clientPath.LastIndexOf("/", StringComparison.Ordinal) + 1);

                return clientPath;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as parent node does not exist", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task DeleteClientAsync(string clientPath)
    {
        var actionToPerform = "delete client znode";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.deleteAsync(clientPath);
                succeeded = true;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, will try again in the next iteration
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired.", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task EnsurePathAsync(string znodePath, byte[] bytesToSet = null)
    {
        var actionToPerform = $"ensure path {znodePath}";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var znodeStat = await zookeeper.existsAsync(znodePath);
                if (znodeStat == null)
                {
                    if (bytesToSet == null)
                    {
                        bytesToSet = Encoding.UTF8.GetBytes("0");
                    }

                    await zookeeper.createAsync(
                        znodePath,
                        bytesToSet,
                        ZooDefs.Ids.OPEN_ACL_UNSAFE,
                        CreateMode.PERSISTENT);
                }

                succeeded = true;
            }
            catch (KeeperException.NodeExistsException)
            {
                succeeded = true; // the node exists which is what we want
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, will try again in the next iteration
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired.", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> IncrementAndWatchEpochAsync(int currentEpoch, Watcher watcher)
    {
        var actionToPerform = "increment epoch";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var data = Encoding.UTF8.GetBytes("0");
                var stat = await zookeeper.setDataAsync(epochPath, data, currentEpoch);

                var dataRes = await zookeeper.getDataAsync(epochPath, watcher);
                if (dataRes.Stat.getVersion() == stat.getVersion())
                {
                    return dataRes.Stat.getVersion();
                }

                throw new ZkStaleVersionException(
                    "Between incrementing the epoch and setting a watch the epoch was incremented");
            }
            catch (KeeperException.BadVersionException e)
            {
                throw new ZkStaleVersionException(
                    $"Could not {actionToPerform} as the current epoch was incremented already.",
                    e);
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> GetEpochAsync()
    {
        var actionToPerform = "get the current epoch";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var dataResult = await zookeeper.getDataAsync(epochPath);
                return dataResult.Stat.getVersion();
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<ClientsZnode> GetActiveClientsAsync()
    {
        var actionToPerform = "get the list of active clients";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var childrenResult = await zookeeper.getChildrenAsync(clientsPath);
                var childrenPaths = childrenResult.Children.Select(x => $"{clientsPath}/{x}").ToList();
                return new ClientsZnode { Version = childrenResult.Stat.getVersion(), ClientPaths = childrenPaths };
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the clients node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<StatusZnode> GetStatusAsync()
    {
        var actionToPerform = "get the status znode";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var dataResult = await zookeeper.getDataAsync(statusPath);
                var status = RebalancingStatus.NotSet;
                if (dataResult.Stat.getDataLength() > 0)
                {
                    status = (RebalancingStatus)BitConverter.ToInt32(dataResult.Data, 0);
                }

                return new StatusZnode { RebalancingStatus = status, Version = dataResult.Stat.getVersion() };
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> SetStatus(StatusZnode statusZnode)
    {
        var actionToPerform = "set the status znode";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var data = BitConverter.GetBytes((int)statusZnode.RebalancingStatus);
                var stat = await zookeeper.setDataAsync(statusPath, data, statusZnode.Version);
                return stat.getVersion();
            }
            catch (KeeperException.BadVersionException e)
            {
                throw new ZkStaleVersionException($"Could not {actionToPerform} due to a bad version number.", e);
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task SetFollowerAsStopped(string clientId)
    {
        var actionToPerform = $"set follower {clientId} as stopped";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.createAsync(
                    $"{stoppedPath}/{clientId}",
                    Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL);

                succeeded = true;
            }
            catch (KeeperException.NodeExistsException)
            {
                succeeded = true;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the stopped znode does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task SetFollowerAsStarted(string clientId)
    {
        var actionToPerform = $"set follower {clientId} as started";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.deleteAsync($"{stoppedPath}/{clientId}");
                succeeded = true;
            }
            catch (KeeperException.NoNodeException)
            {
                succeeded = true;
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<ResourcesZnode> GetResourcesAsync(Watcher childWatcher, Watcher dataWatcher)
    {
        var actionToPerform = "get the list of resources";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                DataResult dataResult = null;
                if (dataWatcher != null)
                {
                    dataResult = await zookeeper.getDataAsync(resourcesPath, dataWatcher);
                }
                else
                {
                    dataResult = await zookeeper.getDataAsync(resourcesPath);
                }

                ChildrenResult childrenResult = null;
                if (childWatcher != null)
                {
                    childrenResult = await zookeeper.getChildrenAsync(resourcesPath, childWatcher);
                }
                else
                {
                    childrenResult = await zookeeper.getChildrenAsync(resourcesPath);
                }

                var resourcesZnodeData = JSONSerializer<ResourcesZnodeData>.DeSerialize(
                    Encoding.UTF8.GetString(dataResult.Data));

                if (resourcesZnodeData == null)
                {
                    resourcesZnodeData = new ResourcesZnodeData();
                }

                return new ResourcesZnode
                {
                    ResourceAssignments = resourcesZnodeData,
                    Resources = childrenResult.Children,
                    Version = dataResult.Stat.getVersion(),
                };
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the resources node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> SetResourcesAsync(ResourcesZnode resourcesZnode)
    {
        var actionToPerform = "set resource assignments";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var data = Encoding.UTF8.GetBytes(
                    JSONSerializer<ResourcesZnodeData>.Serialize(resourcesZnode.ResourceAssignments));
                var stat = await zookeeper.setDataAsync(resourcesPath, data, resourcesZnode.Version);
                return stat.getVersion();
            }
            catch (KeeperException.BadVersionException e)
            {
                throw new ZkStaleVersionException($"Could not {actionToPerform} due to a bad version number.", e);
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} as the node does not exist.", e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task RemoveResourceBarrierAsync(string resource)
    {
        var actionToPerform = $"remove resource barrier on {resource}";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.deleteAsync($"{resourcesPath}/{resource}/barrier");
                succeeded = true;
            }
            catch (KeeperException.NoNodeException)
            {
                succeeded = true;
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task TryPutResourceBarrierAsync(string resource, CancellationToken waitToken, IRebalancerLogger logger)
    {
        Stopwatch sw = new();
        sw.Start();
        var actionToPerform = $"try put resource barrier on {resource}";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.createAsync(
                    $"{resourcesPath}/{resource}/barrier",
                    Encoding.UTF8.GetBytes(clientId),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL);
                succeeded = true;
            }
            catch (KeeperException.NodeExistsException)
            {
                var (exists, owner) = await GetResourceBarrierOwnerAsync(resource);
                if (exists && owner.Equals(clientId))
                {
                    succeeded = true;
                }
                else
                {
                    logger.Info(clientId, $"Waiting for {owner} to release its barrier on {resource}");
                    // wait for two seconds, will retry in next iteration
                    for (var i = 0; i < 20; i++)
                    {
                        await WaitFor(TimeSpan.FromMilliseconds(100));
                        if (waitToken.IsCancellationRequested)
                        {
                            throw new ZkOperationCancelledException(
                                $"Could not {actionToPerform} as the operation was cancelled.");
                        }
                    }
                }
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the resource node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<List<string>> GetStoppedAsync()
    {
        var actionToPerform = "get the list of stopped clients";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var childrenResult = await zookeeper.getChildrenAsync(stoppedPath);
                return childrenResult.Children;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the stopped node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> WatchEpochAsync(Watcher watcher)
    {
        var actionToPerform = "set a watch on epoch";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var stat = await zookeeper.existsAsync(epochPath, watcher);
                return stat.getVersion();
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the epoch node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<StatusZnode> WatchStatusAsync(Watcher watcher)
    {
        var actionToPerform = "set a watch on status";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var dataResult = await zookeeper.getDataAsync(statusPath, watcher);
                return new StatusZnode
                {
                    RebalancingStatus = (RebalancingStatus)BitConverter.ToInt32(dataResult.Data, 0),
                    Version = dataResult.Stat.getVersion(),
                };
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the status node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task WatchResourcesChildrenAsync(Watcher watcher)
    {
        var actionToPerform = "set a watch on resource children";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.getChildrenAsync(resourcesPath, watcher);
                succeeded = true;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the resources node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task<int> WatchResourcesDataAsync(Watcher watcher)
    {
        var actionToPerform = "set a watch on resource data";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var data = await zookeeper.getDataAsync(resourcesPath, watcher);
                return data.Stat.getVersion();
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the resources node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task WatchNodesAsync(Watcher watcher)
    {
        var actionToPerform = "set a watch on clients children";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.getChildrenAsync(clientsPath, watcher);
                succeeded = true;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkInvalidOperationException(
                    $"Could not {actionToPerform} as the clients node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public async Task WatchSiblingNodeAsync(string siblingPath, Watcher watcher)
    {
        var actionToPerform = "set a watch on sibling client";
        var succeeded = false;
        while (!succeeded)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                await zookeeper.getDataAsync(siblingPath, watcher);
                succeeded = true;
            }
            catch (KeeperException.NoNodeException e)
            {
                throw new ZkNoEphemeralNodeWatchException(
                    $"Could not {actionToPerform} as the client node does not exist.",
                    e);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    public override async Task process(WatchedEvent @event)
    {
        keeperState = @event.getState();
        await Task.Yield();
    }

    private async Task<(bool, string)> GetResourceBarrierOwnerAsync(string resource)
    {
        var actionToPerform = "get resource barrier owner";
        while (true)
        {
            await BlockUntilConnected(actionToPerform);

            try
            {
                var dataResult = await zookeeper.getDataAsync($"{resourcesPath}/{resource}/barrier");
                return (true, Encoding.UTF8.GetString(dataResult.Data));
            }
            catch (KeeperException.NoNodeException)
            {
                return (false, string.Empty);
            }
            catch (KeeperException.ConnectionLossException)
            {
                // do nothing, the next iteration will try again
            }
            catch (KeeperException.SessionExpiredException e)
            {
                throw new ZkSessionExpiredException($"Could not {actionToPerform} as the session has expired: ", e);
            }
            catch (Exception e)
            {
                throw new ZkInvalidOperationException($"Could not {actionToPerform} due to an unexpected error", e);
            }
        }
    }

    private async Task BlockUntilConnected(string logAction)
    {
        while (!sessionExpired && !token.IsCancellationRequested && keeperState != Event.KeeperState.SyncConnected)
        {
            if (keeperState == Event.KeeperState.Expired)
            {
                throw new ZkSessionExpiredException($"Could not {logAction} because the session has expired");
            }

            await WaitFor(TimeSpan.FromMilliseconds(100));
        }

        if (token.IsCancellationRequested)
        {
            throw new ZkOperationCancelledException($"Could not {logAction} because the operation was cancelled");
        }

        if (sessionExpired || keeperState == Event.KeeperState.Expired)
        {
            throw new ZkSessionExpiredException($"Could not {logAction} because the session has expired");
        }
    }

    private async Task WaitFor(TimeSpan waitPeriod)
    {
        try
        {
            await Task.Delay(waitPeriod, token);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task WaitFor(TimeSpan waitPeriod, CancellationToken waitToken)
    {
        try
        {
            await Task.Delay(waitPeriod, waitToken);
        }
        catch (TaskCanceledException)
        {
        }
    }
}
