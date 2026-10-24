using Rebalancer.Core;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     Backend-independent scenarios for a SQL Server or Redis node that stops responding longer than the lease /
///     keep-alive expiry and then resumes, or whose store call fails once. The group must hold exactly
///     <see cref="Resources" /> with a short lease expiry.
/// </summary>
internal static class PausedNodeScenarios
{
    public static readonly string[] Resources = ["r1", "r2",];

    // after resuming, a node stops at its first follower tick (heartbeat / 2.5); the margin absorbs slow CI hosts
    private static readonly TimeSpan ReleaseAfterResume = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SafetyWindow = TimeSpan.FromSeconds(15);

    private static readonly ClientOptions Options = new()
    {
        AutoRecoveryOnError = true,
        RestartDelay = TimeSpan.FromSeconds(1),
    };

    /// <summary>
    ///     PRB-0003 / ADR-0019: the coordinator reassigns the resource of a follower paused past the keep-alive expiry
    ///     and records "stop" for it, so the follower's first keep-alive after resuming already says "stop".
    /// </summary>
    public static async Task FollowerPausedPastExpiry_StopsAtFirstTickAfterResume(
        string group,
        Func<IRebalancerProvider> createProvider,
        Func<NodePause, IRebalancerProvider> createPausableProvider
    )
    {
        var pause = new NodePause();
        using var coordinator = new RebalancerClient(createProvider());
        using var follower = new RebalancerClient(createPausableProvider(pause));
        var followerStops = new StopRecorder(follower, pause);
        try
        {
            await coordinator.StartAsync(group, Options);
            await WaitUntilAsync(() => Holds(coordinator, Resources), "the first node never held both resources");
            await follower.StartAsync(group, Options);
            await WaitUntilAsync(
                () => Held(follower).Count == 1 && Held(coordinator).Count == 1,
                "the resources were never split between the two nodes"
            );

            pause.Pause();
            await WaitUntilAsync(
                () => Holds(coordinator, Resources),
                "the coordinator never reassigned the paused follower's resource"
            );
            pause.Resume();

            await WaitUntilAsync(
                () => !Overlaps(coordinator, follower),
                "the resumed follower kept the reassigned resource",
                ReleaseAfterResume
            );
            Assert.True(
                pause.FirstKeepAliveAfterResumeWasStop,
                "the resumed follower's first keep-alive did not say stop (ADR-0019)"
            );
            Assert.True(followerStops.StoppedAfterResume, "the resumed follower did not invoke its stop actions");

            await AssertNeverBothHoldAResourceAsync(coordinator, follower);
        }
        finally
        {
            pause.Resume();
            await coordinator.StopAsync(TimeSpan.FromSeconds(30));
            await follower.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    ///     PRB-0001: a coordinator stalled past its lease expiry loses the lease; the successor takes all resources, and
    ///     the demoted node gives up its own as soon as it resumes.
    /// </summary>
    public static async Task CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume(
        string group,
        Func<IRebalancerProvider> createProvider,
        Func<NodePause, IRebalancerProvider> createPausableProvider
    )
    {
        var pause = new NodePause();
        using var stalled = new RebalancerClient(createPausableProvider(pause));
        using var successor = new RebalancerClient(createProvider());
        var stalledStops = new StopRecorder(stalled, pause);
        try
        {
            await stalled.StartAsync(group, Options);
            await WaitUntilAsync(() => Holds(stalled, Resources), "the first node never held both resources");
            await successor.StartAsync(group, Options);
            await WaitUntilAsync(
                () => Held(stalled).Count == 1 && Held(successor).Count == 1,
                "the resources were never split between the two nodes"
            );

            pause.Pause();
            await WaitUntilAsync(
                () => Holds(successor, Resources),
                "the successor never took over the stalled coordinator's resources"
            );
            pause.Resume();

            await WaitUntilAsync(
                () => !Overlaps(stalled, successor),
                "the demoted coordinator kept its resource",
                ReleaseAfterResume
            );
            Assert.True(stalledStops.StoppedAfterResume, "the demoted coordinator did not invoke its stop actions");

            await AssertNeverBothHoldAResourceAsync(stalled, successor);
        }
        finally
        {
            pause.Resume();
            await stalled.StopAsync(TimeSpan.FromSeconds(30));
            await successor.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    ///     PRB-0005: a follower whose keep-alive fails recovers (auto-recovery) and is assigned again; its record must
    ///     not stay Terminated, which no coordinator includes.
    /// </summary>
    public static async Task FollowerRecoversFromAStoreError_IsAssignedAgain(
        string group,
        Func<IRebalancerProvider> createProvider,
        Func<NodePause, IRebalancerProvider> createPausableProvider
    )
    {
        var faults = new NodePause();
        using var coordinator = new RebalancerClient(createProvider());
        using var follower = new RebalancerClient(createPausableProvider(faults));
        var aborted = 0;
        var assignedAfterAbort = 0;
        follower.OnAborted += (_, _) => Interlocked.Exchange(ref aborted, 1);
        follower.OnAssignment += (_, _) =>
        {
            if (Volatile.Read(ref aborted) == 1)
            {
                Interlocked.Exchange(ref assignedAfterAbort, 1);
            }
        };
        try
        {
            await coordinator.StartAsync(group, Options);
            await WaitUntilAsync(() => Holds(coordinator, Resources), "the first node never held both resources");
            await follower.StartAsync(group, Options);
            await WaitUntilAsync(
                () => Held(follower).Count == 1 && Held(coordinator).Count == 1,
                "the resources were never split between the two nodes"
            );

            faults.FailNextKeepAlive();
            await WaitUntilAsync(
                () => Volatile.Read(ref aborted) == 1,
                "the injected store error never reached the follower"
            );
            await WaitUntilAsync(
                () => Volatile.Read(ref assignedAfterAbort) == 1 &&
                      Held(follower).Count == 1 &&
                      Held(coordinator).Count == 1,
                "the follower was not assigned again after recovering from the error (PRB-0005)"
            );

            await AssertNeverBothHoldAResourceAsync(coordinator, follower);
        }
        finally
        {
            await coordinator.StopAsync(TimeSpan.FromSeconds(30));
            await follower.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    ///     StopAsync raises OnUnassignment before it returns, as documented: a host that stops its handlers there does
    ///     not keep processing resources the group gives to another node.
    /// </summary>
    public static async Task ACleanStop_RaisesOnUnassignmentBeforeItReturns(
        string group,
        Func<IRebalancerProvider> createProvider,
        Func<NodePause, IRebalancerProvider> createPausableProvider
    )
    {
        using var client = new RebalancerClient(createProvider());
        var unassigned = 0;
        client.OnUnassignment += (_, _) => Interlocked.Increment(ref unassigned);
        await client.StartAsync(group, Options);
        await WaitUntilAsync(() => Holds(client, Resources), "the node never held both resources");
        var before = Volatile.Read(ref unassigned);

        await client.StopAsync(TimeSpan.FromSeconds(30));

        Assert.True(Volatile.Read(ref unassigned) > before, "StopAsync returned without raising OnUnassignment");
        Assert.Empty(Held(client));
    }

    /// <summary>
    ///     PRB-0006: a client stopped with StopAsync reports Stopped, as documented, so BlockAsync returns and the health
    ///     check (ADR-0023) reports it Unhealthy.
    /// </summary>
    public static async Task AStoppedClient_ReportsStopped(
        string group,
        Func<IRebalancerProvider> createProvider,
        Func<NodePause, IRebalancerProvider> createPausableProvider
    )
    {
        using var client = new RebalancerClient(createProvider());
        await client.StartAsync(group, Options);
        await WaitUntilAsync(() => Holds(client, Resources), "the node never held both resources");

        await client.StopAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(ClientState.Stopped, client.GetCurrentState());
        Assert.Equal(ClientState.Stopped, client.GetAssignedResources().ClientState);
    }

    private static IList<string> Held(RebalancerClient client) => client.GetAssignedResources().Resources ?? [];

    private static bool Holds(RebalancerClient client, IEnumerable<string> resources) =>
        resources.All(Held(client).Contains);

    private static bool Overlaps(RebalancerClient a, RebalancerClient b) => Held(a).Intersect(Held(b)).Any();

    private static async Task WaitUntilAsync(Func<bool> condition, string failure, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Settle);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(100);
        }
    }

    // once both nodes run normally again, the usual invariant holds: no resource on two nodes at the same observation
    private static async Task AssertNeverBothHoldAResourceAsync(RebalancerClient a, RebalancerClient b)
    {
        var deadline = DateTime.UtcNow + SafetyWindow;
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(Overlaps(a, b), "Invariant violated: both nodes hold the same resource.");
            await Task.Delay(100);
        }
    }

    private sealed class StopRecorder
    {
        private volatile bool stoppedAfterResume;

        public StopRecorder(RebalancerClient client, NodePause pause)
        {
            client.OnUnassignment += (_, _) =>
            {
                if (pause.HasResumed)
                {
                    stoppedAfterResume = true;
                }
            };
        }

        public bool StoppedAfterResume => stoppedAfterResume;
    }
}
