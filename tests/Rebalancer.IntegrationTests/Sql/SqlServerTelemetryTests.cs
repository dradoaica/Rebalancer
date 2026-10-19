using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.Sql;

/// <summary>Requires Docker. ADR-0022 telemetry of the SQL Server backend.</summary>
public sealed class SqlServerTelemetryTests
{
    [SkippableFact]
    public async Task TwoNodes_EmitElectionRebalanceAndAssignments()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-otel-" + Guid.NewGuid().ToString("N");
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources,
            leaseExpirySeconds: 5, heartbeatSeconds: 1);

        await TelemetryScenarios.TwoNodes_EmitElectionRebalanceAndAssignments(
            group, "sqlserver", PausedNodeScenarios.Resources, () => new SqlServerProvider(connectionString)
        );
    }
}
