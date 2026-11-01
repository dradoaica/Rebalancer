using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>Requires Docker. ADR-0023: the health check with a real PostgreSQL client.</summary>
public sealed class PostgreSqlHealthCheckTests
{
    [SkippableFact]
    public async Task TheCheckFollowsARealClient()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-health-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, HealthCheckScenarios.Resources, 5, 1);

        await HealthCheckScenarios.TheCheckFollowsARealClient(group, () => new PostgreSqlProvider(connectionString));
    }
}
