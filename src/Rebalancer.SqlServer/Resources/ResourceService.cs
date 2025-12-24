using Rebalancer.SqlServer.Connections;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer.Resources;

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
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = "SELECT ResourceName FROM [RBR].[Resources] WHERE ResourceGroup = @ResourceGroup";
            command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    resources.Add(reader.GetString(0));
                }
            }
        }

        return resources;
    }
}
