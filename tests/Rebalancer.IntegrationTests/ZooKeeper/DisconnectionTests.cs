using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.ZooKeeper;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper;

/// <summary>Tests that pause the whole ensemble run alone: other ZooKeeper tests would fail meanwhile.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnsemblePauseCollection
{
    public const string Name = "Pauses the ZooKeeper ensemble";
}

/// <summary>
///     Requires the ZooKeeper cluster of <see cref="ZkHelper" />, started as the README describes (containers
///     rbr-it-zk1..3). ADR-0028: a client that loses its connection stops its handlers while the ensemble is unreachable,
///     before ZooKeeper can expire its session, and runs the same resources again when the session survives.
/// </summary>
[Collection(EnsemblePauseCollection.Name)]
public sealed class DisconnectionTests : IDisposable
{
    // the client sees Disconnected after about two thirds of the session timeout; the pause ends before the session
    // timeout, so ZooKeeper keeps the session and the client's own timer (a third of the timeout later) has not fired
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(25);

    private readonly ZkHelper zkHelper = new();

    public void Dispose() => Task.Run(async () => await zkHelper.CloseAsync()).Wait();

    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task ADisconnectedClient_StopsItsHandlersWhileUnreachable_AndResumesWhenTheSessionSurvives(
        RebalancingMode mode
    )
    {
        Skip.IfNot(ZkEnsemble.ContainersRunning(), "The ZooKeeper ensemble containers of the README are not running.");
        var group = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(group, "res", 3);
        using var client = new RebalancerClient(CreateProvider(mode));
        var unassignedWhilePaused = 0;
        var paused = 0;
        client.OnUnassignment += (_, _) =>
        {
            // ReSharper disable once AccessToModifiedClosure
            if (Volatile.Read(ref paused) == 1)
            {
                // ReSharper disable once AccessToModifiedClosure
                Interlocked.Exchange(ref unassignedWhilePaused, 1);
            }
        };

        try
        {
            await client.StartAsync(
                group,
                new ClientOptions
                {
                    AutoRecoveryOnError = true,
                    RestartDelay = TimeSpan.FromSeconds(1),
                }
            );
            await WaitUntilAsync(
                () => Held(client) == 3,
                "the client never held all resources",
                TimeSpan.FromSeconds(60)
            );

            Interlocked.Exchange(ref paused, 1);
            await ZkEnsemble.PauseAsync(ZkEnsemble.Containers);
            try
            {
                await Task.Delay(Pause);
            }
            finally
            {
                await ZkEnsemble.UnpauseAsync(ZkEnsemble.Containers);
                Interlocked.Exchange(ref paused, 0);
            }

            Assert.True(
                Volatile.Read(ref unassignedWhilePaused) == 1,
                "the client kept its handlers running while the ensemble was unreachable (ADR-0028)"
            );
            await WaitUntilAsync(
                () => Held(client) == 3,
                "the client did not run its resources again after the connection came back",
                TimeSpan.FromSeconds(60)
            );
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    private static int Held(RebalancerClient client) => client.GetAssignedResources().Resources?.Count ?? 0;

    private static async Task WaitUntilAsync(Func<bool> condition, string failure, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(200);
        }
    }

    private static ZooKeeperProvider CreateProvider(RebalancingMode mode) =>
        new(
            ZkHelper.zooKeeperHosts,
            "/rebalancer",
            SessionTimeout,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            mode,
            null,
            ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
            null,
            null
        );
}
