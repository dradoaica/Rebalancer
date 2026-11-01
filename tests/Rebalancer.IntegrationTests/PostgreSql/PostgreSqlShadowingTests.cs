using Npgsql;
using Rebalancer.Core;
using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. US-0026 criterion 9, TM-0004 T3: tables with the backend's names in the two schemas of the
///     default search_path, <c>"$user"</c> (a schema named after the login role, searched first) and <c>public</c>, change
///     nothing, because every statement names <c>rbr</c>. (A function cannot shadow a built-in: unqualified names resolve
///     from <c>pg_catalog</c> first.)
/// </summary>
public sealed class PostgreSqlShadowingTests
{
    [SkippableFact]
    public async Task ObjectsWithTheBackendsNamesOnTheSearchPath_AreNotUsed()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-shadow-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, ["r1",], 5, 1);

        // the decoys: the same table names, with a decoy resource, in "$user" and in public
        var user = new NpgsqlConnectionStringBuilder(connectionString).Username!;
        foreach (var schema in new[]
                 {
                     $"\"{user}\"", "public",
                 })
        {
            await PostgreSqlSchema.ExecAsync(
                connectionString,
                $"""
                 CREATE SCHEMA IF NOT EXISTS {schema};
                 CREATE TABLE {schema}.resource_groups (LIKE rbr.resource_groups INCLUDING ALL);
                 CREATE TABLE {schema}.clients (LIKE rbr.clients INCLUDING ALL);
                 CREATE TABLE {schema}.resources (LIKE rbr.resources INCLUDING ALL);
                 INSERT INTO {schema}.resource_groups SELECT * FROM rbr.resource_groups;
                 INSERT INTO {schema}.resources (resource_group, resource_name) VALUES (@g, 'decoy');
                 """,
                ("g", group)
            );
        }

        var assigned = new TaskCompletionSource<IList<string>>();
        using var client = new RebalancerClient(new PostgreSqlProvider(connectionString));
        client.OnAssignment += (_, args) =>
        {
            if (args.Resources.Count > 0)
            {
                assigned.TrySetResult(args.Resources);
            }
        };
        await client.StartAsync(
            group,
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromSeconds(1),
            }
        );

        var resources = await assigned.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["r1",], resources);

        // the lease and the client record carry the real time, written to rbr
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // no client record went to a decoy table
        cmd.CommandText = $"""
                           SELECT (SELECT last_coordinator_renewal FROM rbr.resource_groups WHERE resource_group = @g),
                                  (SELECT max(last_keep_alive) FROM rbr.clients WHERE resource_group = @g),
                                  (SELECT count(*) FROM public.clients) + (SELECT count(*) FROM "{user}".clients)
                           """;
        cmd.Parameters.AddWithValue("g", group);
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            var now = DateTime.UtcNow;
            Assert.InRange(reader.GetDateTime(0), now.AddMinutes(-2), now.AddMinutes(2));
            Assert.InRange(reader.GetDateTime(1), now.AddMinutes(-2), now.AddMinutes(2));
            Assert.Equal(0L, reader.GetInt64(2));
        }

        await client.StopAsync(TimeSpan.FromSeconds(10));
    }
}
