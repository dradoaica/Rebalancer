namespace Rebalancer.IntegrationTests.ZooKeeper.RandomisedTests.TestComponents;

public class AssignmentEvent
{
    public DateTime EventTime { get; init; }
    public required string ClientId { get; init; }
    public required string Action { get; init; }
}
