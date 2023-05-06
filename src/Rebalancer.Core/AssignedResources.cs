namespace Rebalancer.Core;

using System.Collections.Generic;

public class AssignedResources
{
    public ClientState ClientState { get; set; }
    public IList<string> Resources { get; set; }
}
