using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.ResourceManagement;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class ZooKeeperResourceManagerTests
{
    [Fact]
    public async Task RejectedStart_DoesNotBlockLaterStop()
    {
        var manager = new ResourceManager(
            null!,
            new NullRebalancerLogger(),
            new OnChangeActions(),
            RebalancingMode.GlobalBarrier
        );
        await manager.InvokeOnStartActionsAsync("c1", "Follower", [], CancellationToken.None, CancellationToken.None);

        await Assert.ThrowsAsync<InconsistentStateException>(() =>
            manager.InvokeOnStartActionsAsync("c1", "Follower", [], CancellationToken.None, CancellationToken.None)
        );

        var stop = manager.InvokeOnStopActionsAsync("c1", "Follower");
        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(AssignmentStatus.NoAssignmentYet, manager.GetAssignmentStatus());
    }
}
