using System.Collections.Generic;

namespace Rebalancer.LeaseCoordination.Store;

internal class GetResourcesRequest
{
    public AssignmentStatus ResourceAssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
