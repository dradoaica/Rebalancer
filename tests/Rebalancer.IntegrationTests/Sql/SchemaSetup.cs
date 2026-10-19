using Microsoft.Data.SqlClient;

namespace Rebalancer.IntegrationTests.Sql;

internal static class SchemaSetup
{
    public static Task ApplyRbrSchemaAsync(string connectionString, CancellationToken cancellationToken = default) =>
        ApplyScriptAsync(connectionString, "rbr-schema.sql", cancellationToken);

    /// <summary>Runs a script copied to the output's <c>Sql</c> folder, batch by batch (split on <c>GO</c>).</summary>
    public static async Task ApplyScriptAsync(
        string connectionString,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sql", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing {fileName} (ensure Content copy is enabled).", path);
        }

        var script = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var batch in script.Split(
                     new[]
                     {
                         "GO\r\n", "GO\n", "GO ",
                     },
                     StringSplitOptions.RemoveEmptyEntries
                 ))
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = trimmed;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static Task SeedResourceGroupAsync(
        string connectionString,
        string resourceGroup,
        string resourceName,
        CancellationToken cancellationToken = default
    ) => SeedResourceGroupAsync(connectionString, resourceGroup, [resourceName], 300, 25, cancellationToken);

    public static async Task SeedResourceGroupAsync(
        string connectionString,
        string resourceGroup,
        IReadOnlyList<string> resourceNames,
        int leaseExpirySeconds,
        int heartbeatSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                              INSERT INTO RBR.ResourceGroups (ResourceGroup, CoordinatorId, LastCoordinatorRenewal, CoordinatorServer, LockedByClient, FencingToken, LeaseExpirySeconds, HeartbeatSeconds)
                              VALUES (@g, NULL, NULL, NULL, NULL, 0, @expiry, @heartbeat)
                              """;
            cmd.Parameters.AddWithValue("@g", resourceGroup);
            cmd.Parameters.AddWithValue("@expiry", leaseExpirySeconds);
            cmd.Parameters.AddWithValue("@heartbeat", heartbeatSeconds);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var resourceName in resourceNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO RBR.Resources (ResourceGroup, ResourceName) VALUES (@g, @r)";
            cmd.Parameters.AddWithValue("@g", resourceGroup);
            cmd.Parameters.AddWithValue("@r", resourceName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
