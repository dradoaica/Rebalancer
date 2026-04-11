using Rebalancer.Core;
using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>Requires Docker. Validates that two nodes never both report the same resource as assigned (sampled).</summary>
public sealed class SqlServerAssignmentSafetyTests
{
    [SkippableFact]
    public async Task TwoClients_OneResource_AreNeverBothAssignedTheSameResource()
    {
        Skip.IfNot(
            DockerSupport.IsDockerAvailable(),
            "Docker is not available; start Docker to run integration tests."
        );

        await using var sql = new MsSqlBuilder().WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();

        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);

        var group = "it-sql-" + Guid.NewGuid().ToString("N");
        const string resourceName = "only-resource";
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, resourceName);

        var options = new ClientOptions
        {
            AutoRecoveryOnError = true,
            RestartDelay = TimeSpan.FromSeconds(1),
        };

        var backend1 = new SqlServerProvider(connectionString);
        var backend2 = new SqlServerProvider(connectionString);
        using var client1 = new RebalancerClient(backend1);
        using var client2 = new RebalancerClient(backend2);

        await client1.StartAsync(group, options);
        await client2.StartAsync(group, options);

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
                await Task.Delay(200);
            }
        }
        finally
        {
            await client1.StopAsync(TimeSpan.FromSeconds(30));
            await client2.StopAsync(TimeSpan.FromSeconds(30));
        }
    }
}
