using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>Requires Docker. US-0019 randomised assignment safety and liveness on PostgreSQL.</summary>
public sealed class PostgreSqlRandomisedTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task RandomStartsStopsPausesAndKills_KeepAssignmentsSafeAndLive()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-random-" + Guid.NewGuid().ToString("N");
        var resources = RandomisedScenario.ResourceNames(10);
        await PostgreSqlSchema.SeedResourceGroupAsync(
            connectionString,
            group,
            resources,
            RandomisedScenario.LeaseExpirySeconds,
            RandomisedScenario.HeartbeatSeconds
        );

        await new RandomisedScenario(
            group,
            resources,
            pause => PausablePostgreSqlServices.CreateProvider(connectionString, pause),
            output.WriteLine
        ).RunAsync();
    }
}
