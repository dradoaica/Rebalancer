namespace Rebalancer.IntegrationTests.ZooKeeper;

public enum EventType
{
    Assignment,
    Unassignment,
    Error,
}

public class TestEvent
{
    public EventType EventType { get; init; }
    public IList<string> Resources { get; init; }
}
