using Microsoft.Data.SqlClient;
using Rebalancer.Core;
using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>
///     Requires Docker. Nodes whose link to SQL Server is slow and lossy come and go; no resource is ever processed
///     by two nodes at once, and the group converges.
/// </summary>
public sealed class SqlServerNetworkFaultTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-network-" + Guid.NewGuid().ToString("N");
        string[] resources = ["r1", "r2", "r3", "r4", "r5", "r6",];
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, resources, 5, 1);

        await using var proxy = new TcpFaultProxy(sql.Hostname, sql.GetMappedPublicPort(1433));
        proxy.Degrade(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(50),
            0.02,
            TimeSpan.FromMilliseconds(500)
        );
        var throughProxy = new SqlConnectionStringBuilder(connectionString)
        {
            DataSource = $"127.0.0.1,{proxy.Port}",
        }.ConnectionString;

        await NetworkFaultScenarios.NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(
            group,
            resources,
            () => new SqlServerProvider(throughProxy),
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromSeconds(1),
            },
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90),
            output.WriteLine
        );
    }
}
