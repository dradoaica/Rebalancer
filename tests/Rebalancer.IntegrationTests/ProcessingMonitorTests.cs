using Rebalancer.Core;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     US-0024: the pause and resume-window rules of <see cref="ProcessingMonitor" />, with scripted handler events
///     and a manual clock. No backend needed.
/// </summary>
public sealed class ProcessingMonitorTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private DateTime time = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AnOverlapAResumedNodeEndsInsideTheWindow_IsAccepted()
    {
        var (monitor, n1, n2) = await TwoNodesAsync();
        n1.Assign("r1");
        monitor.Pause("n1");
        n2.Assign("r1"); // reassigned during the pause

        monitor.Resume("n1", Window);
        time += TimeSpan.FromSeconds(1);
        n1.Unassign(); // its first tick after the resume
        time += TimeSpan.FromSeconds(5);
        monitor.CheckWindows();

        Assert.Empty(monitor.Overlaps);
        Assert.Equal(["r1",], monitor.ProcessedBy("n2"));
    }

    [Fact]
    public async Task AnOverlapThatOutlastsTheWindow_IsRecorded()
    {
        var (monitor, n1, n2) = await TwoNodesAsync();
        n1.Assign("r1");
        monitor.Pause("n1");
        n2.Assign("r1");

        monitor.Resume("n1", Window);
        time += TimeSpan.FromSeconds(2);
        monitor.CheckWindows();
        Assert.Empty(monitor.Overlaps);
        time += TimeSpan.FromSeconds(2);
        monitor.CheckWindows();

        Assert.Single(monitor.Overlaps);
    }

    [Fact]
    public async Task AResumedNodeStartingAResourceItDidNotHoldBeforeThePause_IsRecordedAtOnce()
    {
        var (monitor, n1, n2) = await TwoNodesAsync();
        n1.Assign("r1");
        n2.Assign("r2");
        monitor.Pause("n1");
        monitor.Resume("n1", Window);

        n1.Assign("r1", "r2");

        Assert.Single(monitor.Overlaps);
    }

    [Fact]
    public async Task HandlerEventsDuringAPause_TakeEffectAtTheResume()
    {
        var (monitor, n1, n2) = await TwoNodesAsync();
        n1.Assign("r1");
        monitor.Pause("n1");
        n1.Unassign(); // a store call in flight when the pause began completes
        n2.Assign("r1");

        Assert.Equal(["r1",], monitor.ProcessedBy("n2"));
        monitor.Resume("n1", Window);
        time += TimeSpan.FromSeconds(5);
        monitor.CheckWindows();

        Assert.Empty(monitor.Overlaps);
        Assert.Empty(monitor.ProcessedBy("n1"));
    }

    [Fact]
    public async Task ACrashedNode_NeverCountsAgain()
    {
        var (monitor, n1, n2) = await TwoNodesAsync();
        n1.Assign("r1");
        monitor.Crash("n1");

        n1.Assign("r1"); // a killed node's client may still raise events
        n2.Assign("r1");

        Assert.Empty(monitor.Overlaps);
        Assert.False(monitor.ProcessedBy("n1").Any());
        Assert.True(monitor.IsProcessed("r1"));
    }

    private async Task<(ProcessingMonitor Monitor, ScriptedNode N1, ScriptedNode N2)> TwoNodesAsync()
    {
        var monitor = new ProcessingMonitor(() => time);
        return (monitor, await ScriptedNode.StartAsync(monitor, "n1"), await ScriptedNode.StartAsync(monitor, "n2"));
    }

    private sealed class ScriptedNode : IRebalancerProvider
    {
        private OnChangeActions actions = null!;

        public Task StartAsync(
            string group,
            OnChangeActions onChangeActions,
            CancellationToken token,
            ClientOptions clientOptions
        )
        {
            actions = onChangeActions;
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

        public static async Task<ScriptedNode> StartAsync(ProcessingMonitor monitor, string name)
        {
            var node = new ScriptedNode();
            var client = new RebalancerClient(node);
            monitor.Attach(client, name);
            await client.StartAsync("group", new ClientOptions());
            return node;
        }

        // as a provider does on a grant: a stop of the previous assignment, then the start of the new one
        public void Assign(params string[] resources)
        {
            Unassign();
            actions.OnStartActions[0](resources.ToList());
        }

        public void Unassign() => actions.OnStopActions[0]();
    }
}
