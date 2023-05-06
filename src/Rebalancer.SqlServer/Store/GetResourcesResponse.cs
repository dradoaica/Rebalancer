namespace Rebalancer.SqlServer.Store;

using System.Collections.Generic;

internal class GetResourcesResponse
{
    public AssignmentStatus AssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
