using Rebalancer.Redis;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using StackExchange.Redis.DataTypes.Collections;

namespace Rebalancer.IntegrationTests.Redis;

internal static class RedisSeed
{
    /// <summary>Provisions a group (lease expiry 5 s, heartbeat 1 s) and its resources in the shared 3.x keys.</summary>
    public static async Task SeedResourceGroupAsync(string connectionString, string group, IEnumerable<string> resourceNames)
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        try
        {
            var db = mux.GetDatabase();
            // without a resource group no node can obtain the lease, so nothing would ever be assigned
            new RedisDictionary<string, ResourceGroup>(db, $"{Constants.SCHEMA}:ResourceGroups")[group] =
                new ResourceGroup
                {
                    Name = group,
                    LeaseExpirySeconds = 5,
                    HeartbeatSeconds = 1,
                };
            var list = new RedisList<Resource>(db, $"{Constants.SCHEMA}:Resources");
            foreach (var resourceName in resourceNames)
            {
                list.Add(
                    new Resource
                    {
                        ResourceGroup = group,
                        ResourceName = resourceName,
                    }
                );
            }
        }
        finally
        {
            mux.Dispose();
        }
    }
}
