using Rebalancer.Core;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     ADR-0022 / US-0017: a client counts assignments and unassignments and measures the time in between, tagged
///     with its backend and group, never with resource names.
/// </summary>
public sealed class RebalancerClientTelemetryTests
{
    [Fact]
    public async Task AssignmentsUnassignmentsAndTheUnassignedDuration_AreRecordedPerGroup()
    {
        var group = "unit-" + Guid.NewGuid().ToString("N");
        using var recorder = new TelemetryRecorder(group);
        var provider = new ScriptedProvider();
        using var client = new RebalancerClient(provider);
        await client.StartAsync(group, new ClientOptions());

        provider.Actions!.OnStartActions[0](["secret-resource",]);
        provider.Actions.OnStopActions[0]();
        await Task.Delay(50);
        provider.Actions.OnStartActions[0](["secret-resource",]);

        Assert.Equal(2, recorder.Sum("rebalancer.assignments"));
        Assert.Equal(1, recorder.Sum("rebalancer.unassignments"));
        var duration = Assert.Single(recorder.Measurements, m => m.Instrument == "rebalancer.unassigned.duration");
        Assert.InRange(duration.Value, 0.04, 10);
        Assert.All(recorder.Measurements, m => Assert.Equal("custom", m.Tags["rebalancer.backend"]));
        Assert.DoesNotContain("secret-resource", recorder.AllTagValues());
    }

    private sealed class ScriptedProvider : IRebalancerProvider
    {
        public OnChangeActions? Actions { get; private set; }

        public Task StartAsync(
            string group,
            OnChangeActions onChangeActions,
            CancellationToken token,
            ClientOptions clientOptions
        )
        {
            Actions = onChangeActions;
            return Task.CompletedTask;
        }

        public Task RecreateClientAsync() => Task.CompletedTask;
        public Task WaitForCompletionAsync() => Task.CompletedTask;

        public AssignedResources GetAssignedResources() => new()
        {
            ClientState = ClientState.Assigned,
            Resources = [],
        };

        public ClientState GetState() => ClientState.Assigned;
    }
}
