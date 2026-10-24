using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.Sql;

/// <summary>Requires Docker. PRB-0007: an assignment delay longer than the keep-alive expiry, on SQL Server.</summary>
public sealed class SqlServerAssignmentDelayTests
{
    [SkippableFact]
    public async Task ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-delay-" + Guid.NewGuid().ToString("N");
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, AssignmentDelayScenarios.Resources, 5, 1);

        await AssignmentDelayScenarios.ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable(
            group,
            () => new SqlServerProvider(connectionString)
        );
    }
}
