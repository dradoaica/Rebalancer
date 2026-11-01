using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination.Resources;

public interface IResourceService
{
    Task<List<string>> GetResourcesAsync(string resourceGroup);
}
