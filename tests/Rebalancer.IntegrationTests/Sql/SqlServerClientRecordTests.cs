using Microsoft.Data.SqlClient;
using Rebalancer.SqlServer.Clients;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.Sql;

/// <summary>Requires Docker. ADR-0014 against a real SQL Server: stale records expire only under the coordinator's lease.</summary>
public sealed class SqlServerClientRecordTests
{
    [SkippableFact]
    public async Task ExpiredRecordsAreDeleted_OnlyWhileTheCoordinatorHoldsTheLease()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var cs = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(cs);
        const string group = "orders";
        await SchemaSetup.SeedResourceGroupAsync(cs, group, "r1");
        var coordinator = Guid.NewGuid();
        var live = Guid.NewGuid();
        var stale = Guid.NewGuid();
        await ExecAsync(
            cs,
            "UPDATE RBR.ResourceGroups SET CoordinatorId = @c, FencingToken = 3 WHERE ResourceGroup = @g",
            ("@c", coordinator),
            ("@g", group)
        );
        foreach (var (id, minutesAgo) in new[]
                 {
                     (coordinator, 120), (live, 1), (stale, 60),
                 })
        {
            await ExecAsync(
                cs,
                """
                INSERT INTO RBR.Clients (ClientId, ResourceGroup, LastKeepAlive, ClientStatus, CoordinatorStatus, Resources, FencingToken)
                VALUES (@id, @g, DATEADD(MINUTE, -@m, GETUTCDATE()), 1, 1, '', 1)
                """,
                ("@id", id),
                ("@g", group),
                ("@m", minutesAgo)
            );
        }

        var service = new ClientService(cs);
        Assert.Equal(
            0,
            await service.DeleteExpiredClientsAsync(group, coordinator, 2, TimeSpan.FromMinutes(10))
        ); // stale token
        Assert.Equal(1, await service.DeleteExpiredClientsAsync(group, coordinator, 3, TimeSpan.FromMinutes(10)));

        var remaining = await QueryIdsAsync(cs, group);
        Assert.Equal(
            new[]
            {
                coordinator, live,
            }.OrderBy(x => x),
            remaining.OrderBy(x => x)
        ); // own record is never deleted
        await service.DeleteClientAsync(live);
        Assert.Equal(
            new[]
            {
                coordinator,
            },
            await QueryIdsAsync(cs, group)
        );
    }

    private static async Task ExecAsync(string cs, string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<Guid>> QueryIdsAsync(string cs, string group)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ClientId FROM RBR.Clients WHERE ResourceGroup = @g";
        cmd.Parameters.AddWithValue("@g", group);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
