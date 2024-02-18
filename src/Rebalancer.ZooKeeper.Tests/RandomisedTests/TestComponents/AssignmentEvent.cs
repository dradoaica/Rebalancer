using System;

namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class AssignmentEvent
{
    public DateTime EventTime { get; init; }
    public string ClientId { get; init; }
    public string Action { get; init; }
}
