using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.Sql;

/// <summary>Requires Docker. US-0019 randomised assignment safety and liveness on SQL Server.</summary>
public sealed class SqlServerRandomisedTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task RandomStartsStopsPausesAndKills_KeepAssignmentsSafeAndLive()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-random-" + Guid.NewGuid().ToString("N");
        var resources = RandomisedScenario.ResourceNames(10);
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, resources,
            RandomisedScenario.LeaseExpirySeconds, RandomisedScenario.HeartbeatSeconds);

        await new RandomisedScenario(
            group, resources, pause => PausableSqlServerServices.CreateProvider(connectionString, pause), output.WriteLine
        ).RunAsync();
    }
}
