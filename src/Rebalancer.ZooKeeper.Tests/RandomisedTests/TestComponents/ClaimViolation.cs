namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class ClaimViolation
{
    public ClaimViolation(string resourceName,
        string currentlyAssignedClientId,
        string claimerClientId)
    {
        this.ResourceName = resourceName;
        this.CurrentlyAssignedClientId = currentlyAssignedClientId;
        this.ClaimerClientId = claimerClientId;
    }

    public string ResourceName { get; set; }
    public string CurrentlyAssignedClientId { get; set; }
    public string ClaimerClientId { get; set; }

    public override string ToString() =>
        $"Client {this.ClaimerClientId} tried to claim the resource {this.ResourceName} still assigned to {this.CurrentlyAssignedClientId}";
}
