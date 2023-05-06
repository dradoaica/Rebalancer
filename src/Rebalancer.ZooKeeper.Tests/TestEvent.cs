namespace Rebalancer.ZooKeeper.Tests;

using System.Collections.Generic;

public enum EventType
{
    Assignment,
    Unassignment,
    Error
}

public class TestEvent
{
    public EventType EventType { get; set; }
    public IList<string> Resources { get; set; }
}
