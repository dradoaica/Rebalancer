using Rebalancer.Core;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     Backend-independent scenarios for network faults between nodes and their store, checked downstream with a
///     <see cref="ProcessingMonitor" />: no resource is ever processed by two nodes at once, and the group converges.
/// </summary>
internal static class NetworkFaultScenarios
{
    public static readonly string[] Nodes = ["n1", "n2", "n3",];

    /// <summary>
    ///     Three nodes whose links to the store are slow and lossy (see <see cref="TcpFaultProxy.Degrade" />) are stopped
    ///     and started at random. The seed is logged; set REBALANCER_RANDOMISED_SEED to replay the same sequence.
    /// </summary>
    public static async Task NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(
        string group,
        IReadOnlyCollection<string> resources,
        Func<IRebalancerProvider> createProvider,
        ClientOptions options,
        TimeSpan churn,
        TimeSpan settle,
        Action<string> log
    )
    {
        var seed = int.TryParse(Environment.GetEnvironmentVariable("REBALANCER_RANDOMISED_SEED"), out var s) ? s
            : Random.Shared.Next();
        var random = new Random(seed);
        log($"seed {seed} (replay: REBALANCER_RANDOMISED_SEED={seed}); group {group}");
        var monitor = new ProcessingMonitor();
        var clients = new Dictionary<string, RebalancerClient>();
        try
        {
            foreach (var node in Nodes)
            {
                await StartAsync(node);
            }

            await WaitUntilAsync(
                () => monitor.Converged(resources, clients.Keys.ToList()),
                "the nodes never converged over the degraded network",
                settle
            );

            var deadline = DateTime.UtcNow + churn;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(4 + random.Next(6)));
                var node = Nodes[random.Next(Nodes.Length)];
                if (clients.ContainsKey(node) && clients.Count > 1)
                {
                    log($"{DateTime.UtcNow:HH:mm:ss.fff} stop {node}");
                    await StopAsync(node);
                }
                else if (!clients.ContainsKey(node))
                {
                    log($"{DateTime.UtcNow:HH:mm:ss.fff} start {node}");
                    await StartAsync(node);
                }
            }

            foreach (var node in Nodes.Where(n => !clients.ContainsKey(n)))
            {
                await StartAsync(node);
            }

            await WaitUntilAsync(
                () => monitor.Converged(resources, clients.Keys.ToList()),
                "the nodes did not converge after the churn",
                settle
            );
            Assert.True(monitor.Overlaps.Count == 0, string.Join(Environment.NewLine, monitor.Overlaps));
        }
        finally
        {
            foreach (var node in clients.Keys.ToList())
            {
                await StopAsync(node);
            }
        }

        async Task StartAsync(string node)
        {
            var client = new RebalancerClient(createProvider());
            monitor.Attach(client, node);
            clients[node] = client;
            await client.StartAsync(group, options);
        }

        async Task StopAsync(string node)
        {
            var client = clients[node];
            clients.Remove(node);
            await client.StopAsync(TimeSpan.FromSeconds(30));
            client.Dispose();
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string failure, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(200);
        }
    }
}
