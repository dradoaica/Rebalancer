using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.ZooKeeper;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.ZooKeeper;

/// <summary>
///     Requires the ZooKeeper cluster of <see cref="ZkHelper" />, started as the README describes (containers
///     rbr-it-zk1..3). Network faults between nodes and the ensemble, and failures of ensemble servers, checked downstream
///     with a <see cref="ProcessingMonitor" />: no resource is ever processed by two nodes at once, and the group
///     converges afterwards. A node's link is faulted through a <see cref="TcpFaultProxy" /> per server, so the other
///     nodes stay connected.
/// </summary>
[Collection(EnsemblePauseCollection.Name)]
public sealed class NetworkFaultTests(ITestOutputHelper output) : IDisposable
{
    // the client reports Disconnected after about two thirds of this; ZooKeeper expires a silent session after it
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(90);
    private static readonly string[] Resources = ["res0", "res1", "res2", "res3", "res4", "res5",];

    private static readonly ClientOptions Options = new()
    {
        AutoRecoveryOnError = true,
        RestartDelay = TimeSpan.FromSeconds(1),
    };

    private readonly List<TcpFaultProxy> proxies = [];
    private readonly ZkHelper zkHelper = new();

    public void Dispose() =>
        Task.Run(async () =>
                {
                    foreach (var proxy in proxies)
                    {
                        await proxy.DisposeAsync();
                    }

                    await zkHelper.CloseAsync();
                }
            )
            .Wait();

    // a node cut off from the ensemble stops its handlers before ZooKeeper expires its session (ADR-0028), so the
    // others take over its resources without overlap; healed, it rejoins with a new session
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier, false)]
    [InlineData(RebalancingMode.ResourceBarrier, true)]
    [InlineData(RebalancingMode.GlobalBarrier, false)]
    [InlineData(RebalancingMode.GlobalBarrier, true)]
    public async Task APartitionedNode_StopsBeforeTheOthersTakeOver_AndRejoinsWhenHealed(
        RebalancingMode mode,
        bool partitionTheCoordinator
    )
    {
        Skip.IfNot(ZkEnsemble.ContainersRunning(), "The ZooKeeper ensemble containers of the README are not running.");
        var group = await PrepareGroupAsync();
        var link = CreateLink();
        var monitor = new ProcessingMonitor();
        var cut = Client(mode, link.Hosts, monitor, "cut");
        var other1 = Client(mode, ZkHelper.zooKeeperHosts, monitor, "n1");
        var other2 = Client(mode, ZkHelper.zooKeeperHosts, monitor, "n2");
        // the first node to register is the coordinator
        RebalancerClient[] order = partitionTheCoordinator ? [cut, other1, other2,] : [other1, cut, other2,];
        try
        {
            await StartInOrderAsync(group, order);
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["cut", "n1", "n2",]),
                "the three nodes never converged"
            );

            link.Partition();
            var partitioned = DateTime.UtcNow;
            await WaitUntilAsync(() => monitor.ProcessedBy("cut").Count == 0, "the partitioned node kept processing");
            var stoppedAfter = DateTime.UtcNow - partitioned;
            output.WriteLine($"the partitioned node stopped processing after {stoppedAfter.TotalSeconds:F1} s");
            // ADR-0028: it stops when it sees the connection drop (about two thirds of the session timeout), well before
            // ZooKeeper can expire its session; without it, the node gave up only at about the session timeout
            Assert.True(
                stoppedAfter < SessionTimeout * 0.85,
                $"the partitioned node stopped only after {stoppedAfter.TotalSeconds:F1} s, too close to the session timeout"
            );
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["n1", "n2",]),
                "the other nodes did not take over the partitioned node's resources"
            );

            link.Heal();
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["cut", "n1", "n2",]),
                "the healed node did not rejoin"
            );

            AssertNoOverlap(monitor);
        }
        finally
        {
            link.Heal();
            await StopAllAsync(order);
        }
    }

    // a node's process dies (its handlers with it) and its link goes silent: its session expires and the others take
    // over its resources
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task ACrashedNode_ItsResourcesMoveOnceItsSessionExpires(RebalancingMode mode)
    {
        Skip.IfNot(ZkEnsemble.ContainersRunning(), "The ZooKeeper ensemble containers of the README are not running.");
        var group = await PrepareGroupAsync();
        var link = CreateLink();
        var monitor = new ProcessingMonitor();
        var crashed = Client(mode, link.Hosts, monitor, "crashed");
        var other1 = Client(mode, ZkHelper.zooKeeperHosts, monitor, "n1");
        var other2 = Client(mode, ZkHelper.zooKeeperHosts, monitor, "n2");
        RebalancerClient[] order = [other1, crashed, other2,];
        try
        {
            await StartInOrderAsync(group, order);
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["crashed", "n1", "n2",]),
                "the three nodes never converged"
            );

            monitor.Crash("crashed");
            link.Partition();
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["n1", "n2",]),
                "the other nodes did not take over the crashed node's resources"
            );

            AssertNoOverlap(monitor);
        }
        finally
        {
            await StopAllAsync([other1, other2,]);
            await crashed.StopAsync(TimeSpan.FromSeconds(5)); // its link stays dead; do not wait for it
            crashed.Dispose();
        }
    }

    // every node's links are slow and lossy while nodes come and go
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(RebalancingMode mode)
    {
        Skip.IfNot(ZkEnsemble.ContainersRunning(), "The ZooKeeper ensemble containers of the README are not running.");
        var group = await PrepareGroupAsync();
        var link = CreateLink();
        link.Degrade(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(50),
            0.02,
            TimeSpan.FromMilliseconds(500)
        );
        await NetworkFaultScenarios.NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(
            group,
            Resources,
            () => Provider(mode, link.Hosts),
            Options,
            TimeSpan.FromSeconds(60),
            Settle,
            output.WriteLine
        );
    }

    // one follower server goes away for longer than the session timeout: its clients move to the other servers
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task LosingAFollowerServer_KeepsProcessingSafe_AndConverges(RebalancingMode mode) =>
        await LoseServersAsync(
            mode,
            async () =>
            {
                var leader = await ZkEnsemble.LeaderAsync();
                return [ZkEnsemble.Containers.First(c => c != leader),];
            },
            TimeSpan.FromSeconds(15)
        );

    // the leader goes away for longer than the session timeout: the other two elect a new one
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task LosingTheLeaderServer_KeepsProcessingSafe_AndConverges(RebalancingMode mode) =>
        await LoseServersAsync(mode, async () => [await ZkEnsemble.LeaderAsync(),], TimeSpan.FromSeconds(15));

    // two of three servers go away for longer than the session timeout: no quorum, so every node is cut off; the nodes
    // stop their handlers and start new sessions once the quorum is back
    [SkippableTheory]
    [InlineData(RebalancingMode.ResourceBarrier)]
    [InlineData(RebalancingMode.GlobalBarrier)]
    public async Task LosingTheQuorumLongerThanTheSessionTimeout_KeepsProcessingSafe_AndConverges(
        RebalancingMode mode
    ) =>
        await LoseServersAsync(
            mode,
            async () =>
            {
                var leader = await ZkEnsemble.LeaderAsync();
                return [leader, ZkEnsemble.Containers.First(c => c != leader),];
            },
            TimeSpan.FromSeconds(25)
        );

    private async Task LoseServersAsync(RebalancingMode mode, Func<Task<string[]>> chooseServers, TimeSpan outage)
    {
        Skip.IfNot(ZkEnsemble.ContainersRunning(), "The ZooKeeper ensemble containers of the README are not running.");
        var group = await PrepareGroupAsync();
        var monitor = new ProcessingMonitor();
        RebalancerClient[] nodes =
        [
            Client(mode, ZkHelper.zooKeeperHosts, monitor, "n1"), Client(mode, ZkHelper.zooKeeperHosts, monitor, "n2"),
            Client(mode, ZkHelper.zooKeeperHosts, monitor, "n3"),
        ];
        try
        {
            await StartInOrderAsync(group, nodes);
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["n1", "n2", "n3",]),
                "the three nodes never converged"
            );

            var servers = await chooseServers();
            output.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} pausing {string.Join(",", servers)} for {outage}");
            await ZkEnsemble.PauseAsync(servers);
            try
            {
                await Task.Delay(outage);
            }
            finally
            {
                await ZkEnsemble.UnpauseAsync(servers);
            }

            await ZkEnsemble.WaitUntilServingAsync(TimeSpan.FromSeconds(60));
            await WaitUntilAsync(
                () => monitor.Converged(Resources, ["n1", "n2", "n3",]),
                "the nodes did not converge after the servers came back"
            );
            AssertNoOverlap(monitor);
        }
        finally
        {
            await StopAllAsync(nodes);
        }
    }

    private async Task<string> PrepareGroupAsync()
    {
        var group = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(group, "res", Resources.Length);
        return group;
    }

    // one proxy per server; a node using these hosts reaches the ensemble only through them
    private Link CreateLink()
    {
        var link = new Link(ZkEnsemble.Ports.Select(port => new TcpFaultProxy("localhost", port)).ToList());
        proxies.AddRange(link.Proxies);
        return link;
    }

    private static RebalancerClient Client(RebalancingMode mode, string hosts, ProcessingMonitor monitor, string node)
    {
        var client = new RebalancerClient(Provider(mode, hosts));
        monitor.Attach(client, node);
        return client;
    }

    private static ZooKeeperProvider Provider(RebalancingMode mode, string hosts) =>
        new(
            hosts,
            "/rebalancer",
            SessionTimeout,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(2),
            mode,
            null,
            ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
            null,
            null
        );

    private static async Task StartInOrderAsync(string group, IEnumerable<RebalancerClient> clients)
    {
        foreach (var client in clients)
        {
            await client.StartAsync(group, Options);
            await Task.Delay(TimeSpan.FromSeconds(1)); // register in this order
        }
    }

    private static async Task StopAllAsync(IEnumerable<RebalancerClient> clients)
    {
        foreach (var client in clients)
        {
            await client.StopAsync(TimeSpan.FromSeconds(30));
            client.Dispose();
        }
    }

    private static Task WaitUntilAsync(Func<bool> condition, string failure) =>
        NetworkFaultScenarios.WaitUntilAsync(condition, failure, Settle);

    private static void AssertNoOverlap(ProcessingMonitor monitor) =>
        Assert.True(monitor.Overlaps.Count == 0, string.Join(Environment.NewLine, monitor.Overlaps));

    private sealed record Link(IReadOnlyList<TcpFaultProxy> Proxies)
    {
        public string Hosts => string.Join(",", Proxies.Select(p => p.Endpoint));

        public void Partition() => Proxies.ToList().ForEach(p => p.Partition());

        public void Heal() => Proxies.ToList().ForEach(p => p.Heal());

        public void Degrade(TimeSpan delay, TimeSpan jitter, double loss, TimeSpan retransmission) =>
            Proxies.ToList().ForEach(p => p.Degrade(delay, jitter, loss, retransmission));
    }
}
