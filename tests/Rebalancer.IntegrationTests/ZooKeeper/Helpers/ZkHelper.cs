using org.apache.zookeeper;
using org.apache.zookeeper.data;
using System.Diagnostics;
using System.Text;

namespace Rebalancer.IntegrationTests.ZooKeeper.Helpers;

public class ZkHelper : Watcher
{
    public static string zooKeeperHosts = "localhost:2181,localhost:2182,localhost:2183";
    private TimeSpan connectTimeout;
    private Event.KeeperState keeperState;
    private TimeSpan sessionTimeout;
    private string zkRootPath = null!; // set by InitializeAsync
    private org.apache.zookeeper.ZooKeeper zookeeper = null!; // set by InitializeAsync


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

    public Task PrepareResourceGroupAsync(string group, string resourcePrefix, int count) =>
        PrepareResourceGroupAsync(group, resourcePrefix, count, ZooDefs.Ids.OPEN_ACL_UNSAFE);

    /// <summary>Creates the group, its resources znode and the resources with <paramref name="acl" />.</summary>
    public async Task PrepareResourceGroupAsync(string group, string resourcePrefix, int count, List<ACL> acl)
    {
        await CreateZnodeAsync($"{zkRootPath}/{group}", acl);
        await CreateZnodeAsync($"{zkRootPath}/{group}/resources", acl);

        for (var i = 0; i < count; i++)
        {
            await CreateZnodeAsync($"{zkRootPath}/{group}/resources/{resourcePrefix}{i}", acl);
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
                    CreateMode.PERSISTENT
                );
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

    /// <summary>Adds credentials to this helper's session, e.g. to read znodes secured with an ACL.</summary>
    public void AddAuthInfo(string scheme, byte[] authData) => zookeeper.addAuthInfo(scheme, authData);

    /// <summary>ACL of a znode below the root path, read by this helper's own session.</summary>
    public async Task<List<ACL>> GetAclAsync(string relativePath) =>
        (await zookeeper.getACLAsync($"{zkRootPath}/{relativePath}")).Acls;

    /// <summary>The client id a resource's barrier znode holds, or null when there is no barrier.</summary>
    public async Task<string?> GetBarrierOwnerAsync(string group, string resourceName)
    {
        try
        {
            var data = await zookeeper.getDataAsync($"{zkRootPath}/{group}/resources/{resourceName}/barrier");
            return Encoding.UTF8.GetString(data.Data);
        }
        catch (KeeperException.NoNodeException)
        {
            return null;
        }
    }

    /// <summary>Children of a znode below the root path, read by this helper's own session.</summary>
    public async Task<List<string>> GetChildrenAsync(string relativePath) =>
        (await zookeeper.getChildrenAsync($"{zkRootPath}/{relativePath}")).Children;

    private Task CreateZnodeAsync(string path) => CreateZnodeAsync(path, ZooDefs.Ids.OPEN_ACL_UNSAFE);

    private async Task CreateZnodeAsync(string path, List<ACL> acl)
    {
        while (true)
        {
            try
            {
                await zookeeper.createAsync(path, new byte[0], acl, CreateMode.PERSISTENT);

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
