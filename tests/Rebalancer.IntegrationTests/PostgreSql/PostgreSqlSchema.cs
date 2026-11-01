using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     A PostgreSQL container with the shipped <c>db/postgresql/</c> scripts applied (US-0026, criterion 3), and the
///     seeding the SQL Server tests do with <c>SchemaSetup</c>.
/// </summary>
internal static class PostgreSqlSchema
{
    /// <summary>Starts a container with the schema and tables; skips the test without Docker.</summary>
    public static async Task<PostgreSqlContainer> StartAsync()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        var container = new PostgreSqlBuilder(DockerSupport.PostgreSqlImage).Build();
        await container.StartAsync();
        await ApplyScriptAsync(container.GetConnectionString(), "RebalanserSchema.sql");
        await ApplyScriptAsync(container.GetConnectionString(), "RebalanserTables.sql");
        return container;
    }

    /// <summary>Runs a script copied to the output's <c>PostgreSql</c> folder, as one batch.</summary>
    public static async Task ApplyScriptAsync(string connectionString, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PostgreSql", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing {fileName} (ensure Content copy is enabled).", path);
        }

        await ExecAsync(connectionString, await File.ReadAllTextAsync(path));
    }

    public static Task SeedResourceGroupAsync(string connectionString, string resourceGroup, string resourceName) =>
        SeedResourceGroupAsync(connectionString, resourceGroup, [resourceName,], 300, 25);

    public static async Task SeedResourceGroupAsync(
        string connectionString,
        string resourceGroup,
        IReadOnlyList<string> resourceNames,
        int leaseExpirySeconds,
        int heartbeatSeconds
    )
    {
        await ExecAsync(
            connectionString,
            """
            INSERT INTO rbr.resource_groups (resource_group, fencing_token, lease_expiry_seconds, heartbeat_seconds)
            VALUES (@g, 0, @expiry, @heartbeat)
            """,
            ("g", resourceGroup),
            ("expiry", leaseExpirySeconds),
            ("heartbeat", heartbeatSeconds)
        );
        foreach (var resourceName in resourceNames)
        {
            await ExecAsync(
                connectionString,
                "INSERT INTO rbr.resources (resource_group, resource_name) VALUES (@g, @r)",
                ("g", resourceGroup),
                ("r", resourceName)
            );
        }
    }

    public static async Task ExecAsync(
        string connectionString,
        string commandText,
        params (string Name, object Value)[] parameters
    )
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<List<Guid>> QueryClientIdsAsync(string connectionString, string group)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT client_id FROM rbr.clients WHERE resource_group = @g";
        cmd.Parameters.AddWithValue("g", group);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
