using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination.Resources;

/// <summary>The resources of a resource group, as provisioned by an operator (ADR-0029).</summary>
public interface IResourceService
{
    Task<List<string>> GetResourcesAsync(string resourceGroup);
}
