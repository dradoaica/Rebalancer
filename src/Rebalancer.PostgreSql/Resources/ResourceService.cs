using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.PostgreSql.Connections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.PostgreSql.Resources;

internal class ResourceService : IResourceService
{
    private readonly string connectionString;

    public ResourceService(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<List<string>> GetResourcesAsync(string resourceGroup)
    {
        List<string> resources = new();
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT resource_name FROM rbr.resources WHERE resource_group = @ResourceGroup";
        command.Parameters.AddWithValue("ResourceGroup", resourceGroup);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resources.Add(reader.GetString(0));
        }

        return resources;
    }
}
