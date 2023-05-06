namespace Rebalancer.ZooKeeper.Tests;

using System.Collections.Generic;

public class RunClientResult
{
    public RunClientResult() => this.AssignedResources = new List<string>();

    public IList<string> AssignedResources { get; set; }
    public bool Assigned { get; set; }
    public bool AssignmentCancelled { get; set; }
    public bool AssignmentErrored { get; set; }
}
