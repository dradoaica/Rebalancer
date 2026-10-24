using Rebalancer.Core;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     PRB-0007: SQL Server or Redis nodes whose <see cref="ClientOptions.OnAssignmentDelay" /> is longer than the
///     group's keep-alive expiry, as the option's documentation suggests for RabbitMQ. The delay may postpone
///     OnAssignment, but the nodes must still converge and then stay put. The group must hold exactly
///     <see cref="Resources" />, with a lease expiry of 5 s and a heartbeat of 1 s.
/// </summary>
internal static class AssignmentDelayScenarios
{
    public static readonly string[] Resources = ["r1", "r2",];
    private static readonly string[] Nodes = ["a", "b",];
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan Stable = TimeSpan.FromSeconds(30);

    private static readonly ClientOptions Options = new()
    {
        AutoRecoveryOnError = true,
        RestartDelay = TimeSpan.FromSeconds(1),
        OnAssignmentDelay = Delay,
    };

    public static async Task ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable(
        string group,
        Func<IRebalancerProvider> createProvider
    )
    {
        var monitor = new ProcessingMonitor();
        var unassignments = 0;
        var clients = Nodes.Select(_ => new RebalancerClient(createProvider())).ToList();
        try
        {
            for (var i = 0; i < Nodes.Length; i++)
            {
                monitor.Attach(clients[i], Nodes[i]);
                clients[i].OnUnassignment += (_, _) => Interlocked.Increment(ref unassignments);
                await clients[i].StartAsync(group, Options);
            }

            await NetworkFaultScenarios.WaitUntilAsync(
                () => monitor.Converged(Resources, Nodes),
                $"the nodes never converged within {Settle.TotalSeconds} s",
                Settle
            );
            var before = Volatile.Read(ref unassignments);
            await Task.Delay(Stable);

            Assert.True(
                Volatile.Read(ref unassignments) == before,
                $"{Volatile.Read(ref unassignments) - before} unassignment(s) in a stable group within " +
                $"{Stable.TotalSeconds} s: live nodes were taken for dead"
            );
            Assert.True(monitor.Overlaps.Count == 0, string.Join(Environment.NewLine, monitor.Overlaps));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.StopAsync(TimeSpan.FromSeconds(30));
                client.Dispose();
            }
        }
    }
}
