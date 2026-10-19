using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.Tests.Shared;
using Rebalancer.ZooKeeper;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper;

/// <summary>
///     Requires the ZooKeeper cluster of <see cref="ZkHelper" />. ADR-0022 / US-0017 for ZooKeeper: two nodes emit an
///     election, a completed rebalance with its activity and coordinator epoch, and assignments; never resource names.
/// </summary>
public sealed class ZooKeeperTelemetryTests : IDisposable
{
    private readonly ZkHelper zkHelper = new();

    public void Dispose() => Task.Run(async () => await zkHelper.CloseAsync()).Wait();

    [Theory]
    [InlineData(RebalancingMode.GlobalBarrier)]
    [InlineData(RebalancingMode.ResourceBarrier)]
    public async Task TwoNodes_EmitElectionRebalanceAndAssignments(RebalancingMode mode)
    {
        var group = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(group, "secret-res", 4);
        using var recorder = new TelemetryRecorder(group);
        using var first = new RebalancerClient(CreateProvider(mode));
        using var second = new RebalancerClient(CreateProvider(mode));
        var options = new ClientOptions
        {
            AutoRecoveryOnError = true,
            RestartDelay = TimeSpan.FromSeconds(1),
        };
        try
        {
            await first.StartAsync(group, options);
            await second.StartAsync(group, options);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline &&
                   !(Held(first) > 0 && Held(second) > 0 && Held(first) + Held(second) == 4))
            {
                await Task.Delay(200);
            }
        }
        finally
        {
            await first.StopAsync(TimeSpan.FromSeconds(30));
            await second.StopAsync(TimeSpan.FromSeconds(30));
        }

        Assert.True(recorder.Sum("rebalancer.coordinator.elections") >= 1, "no coordinator election was recorded");
        Assert.True(recorder.Sum("rebalancer.rebalances", "completed") >= 1, "no completed rebalance was recorded");
        Assert.True(recorder.Sum("rebalancer.assignments") >= 2, "fewer than two assignments were recorded");
        Assert.All(recorder.Measurements, m => Assert.Equal("zookeeper", m.Tags["rebalancer.backend"]));
        var rebalance = recorder.Activities.FirstOrDefault(a => a.OperationName == "rebalancer.rebalance");
        Assert.NotNull(rebalance);
        Assert.NotNull(rebalance.GetTagItem("rebalancer.epoch"));
        Assert.DoesNotContain(
            recorder.AllTagValues(),
            v => v != null && v.StartsWith("secret-res", StringComparison.Ordinal)
        );
    }

    private static int Held(RebalancerClient client) => client.GetAssignedResources().Resources?.Count ?? 0;

    private static ZooKeeperProvider CreateProvider(RebalancingMode mode) =>
        new(
            ZkHelper.zooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            mode,
            null,
            ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
            null,
            null
        );
}
