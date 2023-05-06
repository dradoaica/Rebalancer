namespace Rebalancer.ZooKeeper.ResourceManagement;

using System.Collections.Generic;

public class GetResourcesResponse
{
    public AssignmentStatus AssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
