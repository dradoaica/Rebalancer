namespace Rebalancer.ZooKeeper.Tests.Helpers;

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using org.apache.zookeeper;

public class ZkHelper : Watcher
{
    public static string ZooKeeperHosts = "localhost:2181,localhost:2182,localhost:2183";
    private TimeSpan connectTimeout;
    private Event.KeeperState keeperState;
    private TimeSpan sessionTimeout;
    private string zkRootPath;
    private ZooKeeper zookeeper;


    public async Task InitializeAsync(string zkRootPath, TimeSpan sessionTimeout, TimeSpan connectTimeout)
    {
        this.zkRootPath = zkRootPath;
        this.sessionTimeout = sessionTimeout;
        this.connectTimeout = connectTimeout;
        await this.EstablishSession();
    }

    private async Task EstablishSession()
    {
        this.zookeeper = new ZooKeeper(
            ZooKeeperHosts,
            (int)this.sessionTimeout.TotalMilliseconds,
            this);

        Stopwatch sw = new();
        sw.Start();
        while (this.keeperState != Event.KeeperState.SyncConnected && sw.Elapsed < this.connectTimeout)
        {
            await Task.Delay(50);
        }

        if (this.keeperState != Event.KeeperState.SyncConnected)
        {
            throw new Exception("Could not establish test session");
        }

        await this.EnsureZnodeAsync(this.zkRootPath);
    }

    public async Task CloseAsync()
    {
        if (this.zookeeper != null)
        {
            await this.zookeeper.closeAsync();
        }
    }

    public async Task PrepareResourceGroupAsync(string group, string resourcePrefix, int count)
    {
        await this.CreateZnodeAsync($"{this.zkRootPath}/{group}");
        await this.CreateZnodeAsync($"{this.zkRootPath}/{group}/resources");

        for (var i = 0; i < count; i++)
        {
            await this.CreateZnodeAsync($"{this.zkRootPath}/{group}/resources/{resourcePrefix}{i}");
        }
    }

    public async Task DeleteResourcesAsync(string group, string resourcePrefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await this.SafeDelete($"{this.zkRootPath}/{group}/resources/{resourcePrefix}{i}");
        }
    }

    public async Task AddResourceAsync(string group, string resourceName) =>
        await this.CreateZnodeAsync($"{this.zkRootPath}/{group}/resources/{resourceName}");

    public async Task DeleteResourceAsync(string group, string resourceName)
    {
        while (true)
        {
            try
            {
                var childrenRes =
                    await this.zookeeper.getChildrenAsync($"{this.zkRootPath}/{group}/resources/{resourceName}");
                foreach (var child in childrenRes.Children)
                {
                    await this.SafeDelete($"{this.zkRootPath}/{group}/resources/{resourceName}/{child}");
                }

                await this.SafeDelete($"{this.zkRootPath}/{group}/resources/{resourceName}");
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
                await this.EstablishSession();
            }
        }
    }

    private async Task SafeDelete(string path)
    {
        while (true)
        {
            try
            {
                await this.zookeeper.deleteAsync(path);
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
                await this.EstablishSession();
            }
        }
    }

    public override async Task process(WatchedEvent @event)
    {
        this.keeperState = @event.getState();
        await Task.Yield();
    }

    private async Task EnsureZnodeAsync(string path)
    {
        while (true)
        {
            try
            {
                await this.zookeeper.createAsync(path,
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
                await this.EstablishSession();
            }
        }
    }

    private async Task CreateZnodeAsync(string path)
    {
        while (true)
        {
            try
            {
                await this.zookeeper.createAsync(path,
                    new byte[0],
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
                await this.EstablishSession();
            }
        }
    }
}
