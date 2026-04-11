namespace Rebalancer.IntegrationTests.ZooKeeper;

public class RunClientResult
{
    public IList<string> AssignedResources { get; set; } = new List<string>();
    public bool Assigned { get; set; }
    public bool AssignmentCancelled { get; set; }
    public bool AssignmentErrored { get; set; }
}
