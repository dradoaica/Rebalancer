using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Resources;

internal class ResourceService : IResourceService
{
    private readonly IDatabase cache;

    public ResourceService(IDatabase cache)
    {
        this.cache = cache;
    }

    public async Task<List<string>> GetResourcesAsync(string resourceGroup)
    {
        // only this group's entries since 4.0 (ADR-0020), so the read no longer grows with other groups
        var values = await cache.ListRangeAsync(RedisHash.ResourcesKey(resourceGroup));
        return values.Select(x => RedisHash.Decode<Resource>(x).ResourceName).ToList();
    }
}
