namespace Rebalancer.ZooKeeper.Zk;

using System.Collections.Generic;

public class ClientsZnode
{
    public int Version { get; set; }
    public List<string> ClientPaths { get; set; }
}
