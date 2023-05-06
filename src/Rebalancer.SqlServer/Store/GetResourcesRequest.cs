namespace Rebalancer.SqlServer.Store;

using System.Collections.Generic;

internal class GetResourcesRequest
{
    public AssignmentStatus ResourceAssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
