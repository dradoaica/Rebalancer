using Rebalancer.Core;
using Rebalancer.Tests.Shared;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>ADR-0022 / US-0017 end to end: what two nodes of one group emit on a real backend.</summary>
internal static class TelemetryScenarios
{
    public static async Task TwoNodes_EmitElectionRebalanceAndAssignments(
        string group,
        string backend,
        string[] resources,
        Func<IRebalancerProvider> createProvider
    )
    {
        using var recorder = new TelemetryRecorder(group);
        using var first = new RebalancerClient(createProvider());
        using var second = new RebalancerClient(createProvider());
        var options = new ClientOptions { AutoRecoveryOnError = true, RestartDelay = TimeSpan.FromSeconds(1), };
        try
        {
            await first.StartAsync(group, options);
            await second.StartAsync(group, options);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline &&
                   !(Held(first) + Held(second) == resources.Length && Held(first) > 0 && Held(second) > 0))
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
        Assert.All(recorder.Measurements, m => Assert.Equal(backend, m.Tags["rebalancer.backend"]));
        var rebalance = recorder.Activities.FirstOrDefault(a => a.OperationName == "rebalancer.rebalance");
        Assert.NotNull(rebalance);
        Assert.NotNull(rebalance.GetTagItem("rebalancer.fencing_token"));
        Assert.Empty(recorder.AllTagValues().Intersect(resources));
    }

    private static int Held(RebalancerClient client) => client.GetAssignedResources().Resources?.Count ?? 0;
}
