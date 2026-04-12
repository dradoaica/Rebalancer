using Microsoft.Data.SqlClient;

namespace Rebalancer.IntegrationTests.Sql;

internal static class SchemaSetup
{
    public static async Task ApplyRbrSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sql", "rbr-schema.sql");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Missing rbr-schema.sql (ensure Content copy is enabled).", path);
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

    public static async Task SeedResourceGroupAsync(
        string connectionString,
        string resourceGroup,
        string resourceName,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                              INSERT INTO RBR.ResourceGroups (ResourceGroup, CoordinatorId, LastCoordinatorRenewal, CoordinatorServer, LockedByClient, FencingToken, LeaseExpirySeconds, HeartbeatSeconds)
                              VALUES (@g, NULL, NULL, NULL, NULL, 0, 300, 25)
                              """;
            cmd.Parameters.AddWithValue("@g", resourceGroup);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO RBR.Resources (ResourceGroup, ResourceName) VALUES (@g, @r)";
            cmd.Parameters.AddWithValue("@g", resourceGroup);
            cmd.Parameters.AddWithValue("@r", resourceName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
