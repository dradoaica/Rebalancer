namespace Rebalancer.Redis.Resources;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using StackExchange.Redis.DataTypes.Collections;
using Utils;

internal class ResourceService : IResourceService
{
    private readonly IDatabase cache;

    public ResourceService(IDatabase cache) => this.cache = cache;

    public Task<List<string>> GetResourcesAsync(string resourceGroup)
    {
        var cacheKey = $"{Constants.SCHEMA}:Resources";
        RedisList<Resource> redisList = new(this.cache, cacheKey);
        return Task.FromResult(redisList.Where(x => x.ResourceGroup == resourceGroup).Select(x => x.ResourceName)
            .ToList());
    }
}
