using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Resources;

internal class ResourceService : IResourceService
{
    private static readonly RedisKey ResourcesKey = RedisHash.ListKey("Resources");
    private readonly IDatabase cache;

    public ResourceService(IDatabase cache)
    {
        this.cache = cache;
    }

    public async Task<List<string>> GetResourcesAsync(string resourceGroup)
    {
        var values = await cache.ListRangeAsync(ResourcesKey);
        return values.Select(RedisHash.Decode<Resource>)
            .Where(x => x.ResourceGroup == resourceGroup)
            .Select(x => x.ResourceName)
            .ToList();
    }
}
