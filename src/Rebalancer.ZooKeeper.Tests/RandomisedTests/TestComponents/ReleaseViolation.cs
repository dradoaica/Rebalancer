namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class ReleaseViolation
{
    public ReleaseViolation(string resourceName,
        string currentlyAssignedClientId,
        string releaserClientId)
    {
        this.ResourceName = resourceName;
        this.CurrentlyAssignedClientId = currentlyAssignedClientId;
        this.ReleaserClientId = releaserClientId;
    }

    public string ResourceName { get; set; }
    public string CurrentlyAssignedClientId { get; set; }
    public string ReleaserClientId { get; set; }

    public override string ToString() =>
        $"Client {this.ReleaserClientId} tried to release the resource {this.ResourceName} assigned to {this.CurrentlyAssignedClientId}";
}
