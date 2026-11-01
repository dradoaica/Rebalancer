using System.Collections.Generic;

namespace Rebalancer.LeaseCoordination.Store;

internal class GetResourcesResponse
{
    public AssignmentStatus AssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
