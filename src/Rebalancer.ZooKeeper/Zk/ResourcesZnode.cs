namespace Rebalancer.ZooKeeper.Zk;

using System.Collections.Generic;
using System.Linq;

public class ResourcesZnode
{
    public ResourcesZnode() => this.ResourceAssignments = new ResourcesZnodeData();

    public ResourcesZnodeData ResourceAssignments { get; set; }
    public List<string> Resources { get; set; }
    public int Version { get; set; }

    public bool HasResourceChange()
    {
        if (this.ResourceAssignments.Assignments.Count != this.Resources.Count)
        {
            return true;
        }

        return !this.ResourceAssignments.Assignments
            .Select(x => x.Resource)
            .OrderBy(x => x)
            .SequenceEqual(this.Resources.OrderBy(x => x));
    }
}
