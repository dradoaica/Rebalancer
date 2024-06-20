namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class ReleaseViolation(string resourceName, string currentlyAssignedClientId, string releaserClientId)
{
    public string ResourceName { get; } = resourceName;
    public string CurrentlyAssignedClientId { get; } = currentlyAssignedClientId;
    public string ReleaserClientId { get; } = releaserClientId;

    public override string ToString() =>
        $"Client {ReleaserClientId} tried to release the resource {ResourceName} assigned to {CurrentlyAssignedClientId}";
}
