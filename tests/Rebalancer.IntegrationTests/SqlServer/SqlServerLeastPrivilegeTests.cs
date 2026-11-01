using Microsoft.Data.SqlClient;
using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>
///     Requires Docker. A database installed from the shipped <c>db/sqlserver/</c> scripts works, and
///     <c>db/sqlserver/RebalancerLeastPrivilege.sql</c> grants everything a node needs (TM-0002 follow-up): two nodes
///     whose login holds only that role coordinate, survive a paused follower, and delete their records on a clean stop
///     (ADR-0014).
/// </summary>
public sealed class SqlServerLeastPrivilegeTests
{
    [SkippableFact]
    public async Task NodesWithOnlyTheRebalancerRole_CoordinateAndCleanUp()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var adminConnectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(adminConnectionString);
        await SchemaSetup.ApplyScriptAsync(adminConnectionString, "RebalancerLeastPrivilege.sql");
        var group = "it-sql-role-" + Guid.NewGuid().ToString("N");
        await SchemaSetup.SeedResourceGroupAsync(adminConnectionString, group, PausedNodeScenarios.Resources, 5, 1);

        const string password = "Node_pass_123!";
        await ExecAsync(
            adminConnectionString,
            $"""
             CREATE LOGIN [rebalancer_node_login] WITH PASSWORD = '{password}', CHECK_POLICY = OFF;
             CREATE USER [rebalancer_node_user] FOR LOGIN [rebalancer_node_login];
             ALTER ROLE [rebalancer_node] ADD MEMBER [rebalancer_node_user];
             """
        );
        var nodeConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            UserID = "rebalancer_node_login",
            Password = password,
        }.ConnectionString;

        await PausedNodeScenarios.FollowerPausedPastExpiry_StopsAtFirstTickAfterResume(
            group,
            () => new SqlServerProvider(nodeConnectionString),
            pause => PausableSqlServerServices.CreateProvider(nodeConnectionString, pause)
        );

        // both nodes stopped cleanly, which needs DELETE on RBR.Clients
        Assert.Equal(0, await CountClientsAsync(adminConnectionString, group));
    }

    private static async Task ExecAsync(string connectionString, string commandText)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountClientsAsync(string connectionString, string group)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM RBR.Clients WHERE ResourceGroup = @g";
        cmd.Parameters.AddWithValue("@g", group);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
