namespace Rebalancer.IntegrationTests.ZooKeeper.RandomisedTests.TestComponents;

public class AssignmentEvent
{
    public DateTime EventTime { get; init; }
    public string ClientId { get; init; }
    public string Action { get; init; }
}
