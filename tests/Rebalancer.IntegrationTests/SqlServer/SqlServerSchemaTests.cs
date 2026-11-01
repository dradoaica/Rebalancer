using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>
///     Requires Docker. PRB-0008: on the shipped tables a resource's group and name together are at most 900 bytes, the
///     limit of SQL Server's clustered key <c>QueuesPK</c> (the README states it).
/// </summary>
public sealed class SqlServerSchemaTests
{
    private const int KeyLimitBytes = 900;

    [SkippableFact]
    public async Task AResourceKey_FitsUpTo900Bytes_AndALongerOneIsRejected()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var cs = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(cs);
        const string group = "orders";
        await SchemaSetup.SeedResourceGroupAsync(cs, group, ["r1",], 300, 25);

        await InsertResourceAsync(cs, group, new string('a', KeyLimitBytes - group.Length));
        var tooLong = await Assert.ThrowsAsync<SqlException>(() =>
            InsertResourceAsync(cs, group, new string('b', KeyLimitBytes - group.Length + 1))
        );
        Assert.Equal(1946, tooLong.Number); // the index entry exceeds the maximum key length
    }

    private static async Task InsertResourceAsync(string cs, string group, string resourceName)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO RBR.Resources (ResourceGroup, ResourceName) VALUES (@g, @r)";
        cmd.Parameters.AddWithValue("@g", group);
        cmd.Parameters.AddWithValue("@r", resourceName);
        await cmd.ExecuteNonQueryAsync();
    }
}
