namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class ClaimViolation(
    string resourceName,
    string currentlyAssignedClientId,
    string claimerClientId)
{
    public string ResourceName { get; } = resourceName;
    public string CurrentlyAssignedClientId { get; } = currentlyAssignedClientId;
    public string ClaimerClientId { get; } = claimerClientId;

    public override string ToString()
    {
        return
            $"Client {ClaimerClientId} tried to claim the resource {ResourceName} still assigned to {CurrentlyAssignedClientId}";
    }
}
