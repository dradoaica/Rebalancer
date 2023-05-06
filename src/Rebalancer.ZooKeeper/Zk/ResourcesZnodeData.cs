namespace Rebalancer.ZooKeeper.Zk;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

[Serializable]
[DataContract(Namespace = "Rebalancer", Name = "ResourcesZnodeData")]
public class ResourcesZnodeData
{
    public ResourcesZnodeData() => this.Assignments = new List<ResourceAssignment>();

    [DataMember(Name = "Assignments")] public List<ResourceAssignment> Assignments { get; set; }
}
