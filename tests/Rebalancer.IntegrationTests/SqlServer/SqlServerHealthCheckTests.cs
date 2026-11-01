using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>Requires Docker. ADR-0023: the health check with a real SQL Server client.</summary>
public sealed class SqlServerHealthCheckTests
{
    [SkippableFact]
    public async Task TheCheckFollowsARealClient()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-health-" + Guid.NewGuid().ToString("N");
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, HealthCheckScenarios.Resources, 5, 1);

        await HealthCheckScenarios.TheCheckFollowsARealClient(group, () => new SqlServerProvider(connectionString));
    }
}
