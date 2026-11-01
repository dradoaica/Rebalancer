using Npgsql;
using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. A database installed from the shipped <c>db/postgresql/</c> scripts works, and
///     <c>RebalancerLeastPrivilege.sql</c> grants everything a node needs (US-0026 criterion 3, TM-0004): two nodes whose
///     login is only a member of that role coordinate, survive a paused follower, and delete their records on a clean stop
///     (ADR-0014).
/// </summary>
public sealed class PostgreSqlLeastPrivilegeTests
{
    [SkippableFact]
    public async Task NodesWithOnlyTheRebalancerRole_CoordinateAndCleanUp()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var adminConnectionString = pg.GetConnectionString();
        await PostgreSqlSchema.ApplyScriptAsync(adminConnectionString, "RebalancerLeastPrivilege.sql");
        var group = "it-pg-role-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(
            adminConnectionString,
            group,
            PausedNodeScenarios.Resources,
            5,
            1
        );

        const string password = "node_pass_123";
        await PostgreSqlSchema.ExecAsync(
            adminConnectionString,
            $"CREATE ROLE rebalancer_node_login LOGIN PASSWORD '{password}' IN ROLE rebalancer_node"
        );
        var nodeConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "rebalancer_node_login",
            Password = password,
        }.ConnectionString;

        await PausedNodeScenarios.FollowerPausedPastExpiry_StopsAtFirstTickAfterResume(
            group,
            () => new PostgreSqlProvider(nodeConnectionString),
            pause => PausablePostgreSqlServices.CreateProvider(nodeConnectionString, pause)
        );

        // both nodes stopped cleanly, which needs DELETE on rbr.clients
        Assert.Empty(await PostgreSqlSchema.QueryClientIdsAsync(adminConnectionString, group));

        // and nothing beyond that: creating groups and resources stays with an operator (US-0026 criterion 3)
        foreach (var insert in new[]
                 {
                     "INSERT INTO rbr.resource_groups (resource_group, lease_expiry_seconds, heartbeat_seconds) " +
                     "VALUES ('x', 5, 1)",
                     $"INSERT INTO rbr.resources (resource_group, resource_name) VALUES ('{group}', 'x')",
                 })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlSchema.ExecAsync(nodeConnectionString, insert)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
    }
}
