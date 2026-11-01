using Npgsql;
using Rebalancer.Core;
using Rebalancer.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. Nodes whose link to PostgreSQL is slow and lossy come and go; no resource is ever processed
///     by two nodes at once, and the group converges.
/// </summary>
[Collection(NetworkFaultCollection.Name)]
public sealed class PostgreSqlNetworkFaultTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-network-" + Guid.NewGuid().ToString("N");
        string[] resources = ["r1", "r2", "r3", "r4", "r5", "r6",];
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, resources, 5, 1);

        await using var proxy = new TcpFaultProxy(pg.Hostname, pg.GetMappedPublicPort(5432));
        proxy.Degrade(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(50),
            0.02,
            TimeSpan.FromMilliseconds(500)
        );
        var throughProxy = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = "127.0.0.1",
            Port = proxy.Port,
        }.ConnectionString;

        await NetworkFaultScenarios.NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(
            group,
            resources,
            () => new PostgreSqlProvider(throughProxy),
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
