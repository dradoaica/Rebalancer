using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>Requires Docker. ADR-0022 telemetry of the PostgreSQL backend.</summary>
public sealed class PostgreSqlTelemetryTests
{
    [SkippableFact]
    public async Task TwoNodes_EmitElectionRebalanceAndAssignments()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-otel-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources, 5, 1);

        await TelemetryScenarios.TwoNodes_EmitElectionRebalanceAndAssignments(
            group,
            "postgresql",
            PausedNodeScenarios.Resources,
            () => new PostgreSqlProvider(connectionString)
        );
    }
}
