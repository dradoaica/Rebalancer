namespace Rebalancer.Redis.Resources;

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IResourceService
{
    Task<List<string>> GetResourcesAsync(string resourceGroup);
}
