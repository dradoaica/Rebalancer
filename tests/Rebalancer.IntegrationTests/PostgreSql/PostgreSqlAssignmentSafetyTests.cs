using Rebalancer.Core;
using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. Validates that two nodes never both report the same resource as assigned (sampled), and never
///     process it at the same time (handler events, <see cref="ProcessingMonitor" />).
/// </summary>
public sealed class PostgreSqlAssignmentSafetyTests
{
    [SkippableFact]
    public async Task TwoClients_OneResource_AreNeverBothAssignedTheSameResource()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();

        var group = "it-pg-" + Guid.NewGuid().ToString("N");
        const string resourceName = "only-resource";
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, resourceName);

        var options = new ClientOptions
        {
            AutoRecoveryOnError = true,
            RestartDelay = TimeSpan.FromSeconds(1),
        };

        var backend1 = new PostgreSqlProvider(connectionString);
        var backend2 = new PostgreSqlProvider(connectionString);
        using var client1 = new RebalancerClient(backend1);
        using var client2 = new RebalancerClient(backend2);
        // US-0024 follow-up: also what the clients process, not only what they report
        var monitor = new ProcessingMonitor();
        monitor.Attach(client1, "client1");
        monitor.Attach(client2, "client2");

        await client1.StartAsync(group, options);
        await client2.StartAsync(group, options);

        var assignedObservations = 0;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                var a = client1.GetAssignedResources().Resources ?? [];
                var b = client2.GetAssignedResources().Resources ?? [];
                Assert.False(
                    a.Contains(resourceName) && b.Contains(resourceName),
                    "Invariant violated: both clients hold the same resource."
                );
                if (a.Contains(resourceName) || b.Contains(resourceName))
                {
                    assignedObservations++;
                }

                await Task.Delay(200);
            }

            // without this the safety check passes vacuously when nothing is ever assigned
            Assert.True(assignedObservations > 0, "Liveness violated: the resource was never assigned to any client.");
            Assert.True(monitor.Overlaps.Count == 0, string.Join(Environment.NewLine, monitor.Overlaps));
            Assert.True(monitor.IsProcessed(resourceName), "no client processes the resource");
        }
        finally
        {
            await client1.StopAsync(TimeSpan.FromSeconds(30));
            await client2.StopAsync(TimeSpan.FromSeconds(30));
        }
    }
}
