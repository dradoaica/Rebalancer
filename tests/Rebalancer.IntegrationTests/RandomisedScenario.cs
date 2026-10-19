using Rebalancer.Core;
using System.Globalization;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     US-0019: N nodes of one SQL Server or Redis group (lease expiry 5 s, heartbeat 1 s) are started, stopped, paused
///     and killed at random while a monitor samples their assignments every 100 ms. It fails when
///     <list type="bullet">
///         <item>two running nodes hold the same resource (safety), or</item>
///         <item>
///             a resource stays unassigned longer than <see cref="UnassignedBound" /> while a node is running
///             (liveness).
///         </item>
///     </list>
///     A paused node is frozen (its store calls wait, <see cref="NodePause" />); a killed node is paused until the end.
///     Neither takes part in the checks, and a resumed node is excluded for <see cref="ResumeGrace" />, the window
///     ADR-0019 and ADR-0021 accept before it stops at its first tick. The seed is logged; set REBALANCER_RANDOMISED_SEED
///     to replay the same sequence of actions (timings still vary). REBALANCER_RANDOMISED_TEST_MAX_MINUTES caps the
///     duration, as for the ZooKeeper suite.
/// </summary>
internal sealed class RandomisedScenario
{
    public const int LeaseExpirySeconds = 5;
    public const int HeartbeatSeconds = 1;
    private const int NodeCount = 5;
    private const int MaxKilled = 2;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ResumeGrace = TimeSpan.FromSeconds(3);

    // a rebalance waits 5 s per barrier check (Roles/Coordinator.cs) on top of the lease and keep-alive expiry
    private static readonly TimeSpan UnassignedBound = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinActionInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxActionInterval = TimeSpan.FromSeconds(20);

    private static readonly ClientOptions Options = new()
    {
        AutoRecoveryOnError = true,
        RestartDelay = TimeSpan.FromSeconds(1),
    };

    private readonly Func<NodePause, IRebalancerProvider> createProvider;
    private readonly string group;
    private readonly Action<string> log;
    private readonly List<Node> nodes = [];
    private readonly Random random;
    private readonly string[] resources;
    private readonly int seed;
    private readonly Dictionary<string, DateTime> unassignedSince = new();

    public RandomisedScenario(
        string group,
        string[] resources,
        Func<NodePause, IRebalancerProvider> createProvider,
        Action<string> log
    )
    {
        this.group = group;
        this.resources = resources;
        this.createProvider = createProvider;
        this.log = log;
        seed = int.TryParse(Environment.GetEnvironmentVariable("REBALANCER_RANDOMISED_SEED"), out var s) ? s
            : Random.Shared.Next();
        random = new Random(seed);
    }

    public static string[] ResourceNames(int count) => Enumerable.Range(1, count).Select(i => $"r{i}").ToArray();

    public async Task RunAsync()
    {
        var duration = Duration();
        log($"seed {seed} (replay: REBALANCER_RANDOMISED_SEED={seed}); duration {duration}; group {group}");
        for (var i = 0; i < NodeCount; i++)
        {
            nodes.Add(new Node(i));
        }

        try
        {
            foreach (var node in nodes)
            {
                await StartAsync(node);
            }

            var end = DateTime.UtcNow + duration;
            var nextAction = DateTime.UtcNow + NextInterval();
            while (DateTime.UtcNow < end)
            {
                ResumeDuePauses();
                Check();
                if (DateTime.UtcNow >= nextAction)
                {
                    await ActAsync();
                    nextAction = DateTime.UtcNow + NextInterval();
                }

                await Task.Delay(100);
            }
        }
        finally
        {
            foreach (var node in nodes.Where(n => n.Client != null))
            {
                node.Pause.Resume();
                await node.Client!.StopAsync(TimeSpan.FromSeconds(30));
                node.Client.Dispose();
            }
        }
    }

    private async Task ActAsync()
    {
        var running = nodes.Where(n => n.State == NodeState.Running).ToList();
        var stopped = nodes.Where(n => n.State == NodeState.Stopped).ToList();
        var roll = random.Next(4);
        if (roll == 0 && stopped.Any())
        {
            await StartAsync(stopped[random.Next(stopped.Count)]);
        }
        else if (running.Count > 1) // always keep one node running
        {
            var node = running[random.Next(running.Count)];
            if (roll == 1)
            {
                log($"{Now()} stop node {node.Id}");
                node.State = NodeState.Stopping;
                await node.Client!.StopAsync(TimeSpan.FromSeconds(30));
                node.Client.Dispose();
                node.Client = null;
                node.State = NodeState.Stopped;
            }
            else if (roll == 2 || nodes.Count(n => n.State == NodeState.Killed) >= MaxKilled)
            {
                var pause = TimeSpan.FromSeconds(random.Next(2, 13)); // shorter and longer than the lease expiry
                log($"{Now()} pause node {node.Id} for {pause.TotalSeconds} s");
                node.Pause.Pause();
                node.State = NodeState.Paused;
                node.ResumeAt = DateTime.UtcNow + pause;
            }
            else
            {
                log($"{Now()} kill node {node.Id}");
                node.Pause.Pause();
                node.State = NodeState.Killed;
            }
        }
    }

    private async Task StartAsync(Node node)
    {
        log($"{Now()} start node {node.Id}");
        node.Pause = new NodePause();
        node.Client = new RebalancerClient(createProvider(node.Pause));
        await node.Client.StartAsync(group, Options);
        node.State = NodeState.Running;
        node.ResumedAt = null;
    }

    private void ResumeDuePauses()
    {
        foreach (var node in nodes.Where(n => n.State == NodeState.Paused && DateTime.UtcNow >= n.ResumeAt))
        {
            log($"{Now()} resume node {node.Id}");
            node.Pause.Resume();
            node.State = NodeState.Running;
            node.ResumedAt = DateTime.UtcNow;
        }
    }

    private void Check()
    {
        var now = DateTime.UtcNow;
        var checkedNodes = nodes.Where(n => n.State == NodeState.Running && !(now - n.ResumedAt < ResumeGrace))
            .ToList();
        var holders = new Dictionary<string, int>();
        foreach (var node in checkedNodes)
        {
            foreach (var resource in node.Client!.GetAssignedResources().Resources ?? [])
            {
                Assert.False(
                    holders.TryGetValue(resource, out var other),
                    $"Safety violated at {Now()}: nodes {other} and {node.Id} both hold {resource} (seed {seed})"
                );
                holders[resource] = node.Id;
            }
        }

        foreach (var resource in resources)
        {
            if (holders.ContainsKey(resource) || !checkedNodes.Any())
            {
                unassignedSince.Remove(resource);
                continue;
            }

            unassignedSince.TryAdd(resource, now);
            Assert.True(
                now - unassignedSince[resource] <= UnassignedBound,
                $"Liveness violated at {Now()}: {resource} unassigned for more than {UnassignedBound.TotalSeconds} s " +
                $"while nodes {string.Join(",", checkedNodes.Select(n => n.Id))} run (seed {seed})"
            );
        }
    }

    private TimeSpan NextInterval() =>
        MinActionInterval +
        TimeSpan.FromMilliseconds(random.Next((int)(MaxActionInterval - MinActionInterval).TotalMilliseconds));

    private static TimeSpan Duration()
    {
        var maxMinutes = Environment.GetEnvironmentVariable("REBALANCER_RANDOMISED_TEST_MAX_MINUTES");
        return double.TryParse(maxMinutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) &&
               minutes > 0 &&
               TimeSpan.FromMinutes(minutes) < DefaultDuration ? TimeSpan.FromMinutes(minutes) : DefaultDuration;
    }

    private static string Now() => DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private enum NodeState { Stopped, Running, Paused, Killed, Stopping, }

    private sealed class Node(int id)
    {
        public int Id { get; } = id;
        public NodePause Pause { get; set; } = new();
        public RebalancerClient? Client { get; set; }
        public NodeState State { get; set; } = NodeState.Stopped;
        public DateTime ResumeAt { get; set; }
        public DateTime? ResumedAt { get; set; }
    }
}
