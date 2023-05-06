namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

using System;

public class AssignmentEvent
{
    public DateTime EventTime { get; set; }
    public string ClientId { get; set; }
    public string Action { get; set; }
}
