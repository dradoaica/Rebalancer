using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using org.apache.zookeeper;

namespace Rebalancer.ZooKeeper.Tests.Helpers;

public class ZkHelper : Watcher
{
    public static string zooKeeperHosts = "localhost:2181,localhost:2182,localhost:2183";
    private TimeSpan connectTimeout;
    private Event.KeeperState keeperState;
    private TimeSpan sessionTimeout;
    private string zkRootPath;
    private org.apache.zookeeper.ZooKeeper zookeeper;


    public async Task InitializeAsync(string zkRootPath, TimeSpan sessionTimeout, TimeSpan connectTimeout)
    {
        this.zkRootPath = zkRootPath;
        this.sessionTimeout = sessionTimeout;
        this.connectTimeout = connectTimeout;
        await EstablishSession();
    }

    private async Task EstablishSession()
    {
        zookeeper = new org.apache.zookeeper.ZooKeeper(zooKeeperHosts, (int)sessionTimeout.TotalMilliseconds, this);

        Stopwatch sw = new();
        sw.Start();
        while (keeperState != Event.KeeperState.SyncConnected && sw.Elapsed < connectTimeout)
        {
            await Task.Delay(50);
        }

        if (keeperState != Event.KeeperState.SyncConnected)
        {
            throw new Exception("Could not establish test session");
        }

        await EnsureZnodeAsync(zkRootPath);
    }

    public async Task CloseAsync()
    {
        if (zookeeper != null)
        {
            await zookeeper.closeAsync();
        }
    }

    public async Task PrepareResourceGroupAsync(string group, string resourcePrefix, int count)
    {
        await CreateZnodeAsync($"{zkRootPath}/{group}");
        await CreateZnodeAsync($"{zkRootPath}/{group}/resources");

        for (var i = 0; i < count; i++)
        {
            await CreateZnodeAsync($"{zkRootPath}/{group}/resources/{resourcePrefix}{i}");
        }
    }

    public async Task DeleteResourcesAsync(string group, string resourcePrefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await SafeDelete($"{zkRootPath}/{group}/resources/{resourcePrefix}{i}");
        }
    }

    public async Task AddResourceAsync(string group, string resourceName) =>
        await CreateZnodeAsync($"{zkRootPath}/{group}/resources/{resourceName}");

    public async Task DeleteResourceAsync(string group, string resourceName)
    {
        while (true)
        {
            try
            {
                var childrenRes = await zookeeper.getChildrenAsync($"{zkRootPath}/{group}/resources/{resourceName}");
                foreach (var child in childrenRes.Children)
                {
                    await SafeDelete($"{zkRootPath}/{group}/resources/{resourceName}/{child}");
                }

                await SafeDelete($"{zkRootPath}/{group}/resources/{resourceName}");
                return;
            }
            catch (KeeperException.NoNodeException)
            {
                return;
            }
            catch (KeeperException.ConnectionLossException)
            {
            }
            catch (KeeperException.SessionExpiredException)
            {
                await EstablishSession();
            }
        }
    }

    private async Task SafeDelete(string path)
    {
        while (true)
        {
            try
            {
                await zookeeper.deleteAsync(path);
                return;
            }
            catch (KeeperException.NoNodeException)
            {
                return;
            }
            catch (KeeperException.NotEmptyException)
            {
                return;
            }
            catch (KeeperException.ConnectionLossException)
            {
            }
            catch (KeeperException.SessionExpiredException)
            {
                await EstablishSession();
            }
        }
    }

    public override async Task process(WatchedEvent @event)
    {
        keeperState = @event.getState();
        await Task.Yield();
    }

    private async Task EnsureZnodeAsync(string path)
    {
        while (true)
        {
            try
            {
                await zookeeper.createAsync(
                    path,
                    Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.PERSISTENT);
                return;
            }
            catch (KeeperException.NodeExistsException)
            {
                return;
            }
            catch (KeeperException.ConnectionLossException)
            {
            }
            catch (KeeperException.SessionExpiredException)
            {
                await EstablishSession();
            }
        }
    }

    private async Task CreateZnodeAsync(string path)
    {
        while (true)
        {
            try
            {
                await zookeeper.createAsync(path, new byte[0], ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);

                return;
            }
            catch (KeeperException.NodeExistsException)
            {
                return;
            }
            catch (KeeperException.ConnectionLossException)
            {
            }
            catch (KeeperException.SessionExpiredException)
            {
                await EstablishSession();
            }
        }
    }
}
