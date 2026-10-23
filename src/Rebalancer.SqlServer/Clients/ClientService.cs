using Microsoft.Data.SqlClient;
using Rebalancer.SqlServer.Connections;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer.Clients;

internal class ClientService : IClientService, IFencedClientService
{
    // the fencing guard of a coordinator write (ADR-0014, ADR-0026): the lease still names this coordinator and token
    private const string LeaseGuard = @"EXISTS (SELECT 1 FROM [RBR].[ResourceGroups]
              WHERE ResourceGroup = @ResourceGroup
                AND CoordinatorId = @CoordinatorId
                AND FencingToken = @FencingToken)";

    private readonly string connectionString;

    public ClientService(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task CreateClientAsync(string resourceGroup, Guid clientId)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = @"INSERT INTO [RBR].[Clients]
           ([ClientId]
           ,[ResourceGroup]
           ,[LastKeepAlive]
           ,[ClientStatus]
           ,[CoordinatorStatus]
           ,[Resources]
           ,[FencingToken])
     VALUES
           (@ClientId
           ,@ResourceGroup
           ,GETUTCDATE()
           ,@ClientStatus
           ,@CoordinatorStatus
           ,''
           ,1)";
            command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
            command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("@ClientStatus", SqlDbType.TinyInt).Value = ClientStatus.Waiting;
            command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.StopActivity;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<Client>> GetActiveClientsAsync(string resourceGroup)
    {
        List<Client> clients = new();
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = @"SELECT [ClientId]
      ,[LastKeepAlive]
      ,[ClientStatus]
      ,[CoordinatorStatus]
      ,[FencingToken]
      ,GETUTCDATE() AS [TimeNow]
FROM [RBR].[Clients] 
WHERE ResourceGroup = @ResourceGroup
AND (ClientStatus = 0 OR ClientStatus = 1)";
            command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Client client = new()
                    {
                        ClientStatus = (ClientStatus)(byte)reader["ClientStatus"],
                        CoordinatorStatus = (CoordinatorStatus)(byte)reader["CoordinatorStatus"],
                        ClientId = (Guid)reader["ClientId"],
                        LastKeepAlive = (DateTime)reader["LastKeepAlive"],
                        FencingToken = (int)reader["FencingToken"],
                        TimeNow = (DateTime)reader["TimeNow"],
                    };
                    clients.Add(client);
                }
            }
        }

        return clients;
    }

    public async Task<Client> KeepAliveAsync(Guid clientId)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = @"UPDATE [RBR].[Clients]
   SET [LastKeepAlive] = GETUTCDATE()
   OUTPUT inserted.*
WHERE ClientId = @ClientId";
            command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
            using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    Client client = new()
                    {
                        ClientId = clientId,
                        ClientStatus = (ClientStatus)(byte)reader["ClientStatus"],
                        CoordinatorStatus = (CoordinatorStatus)(byte)reader["CoordinatorStatus"],
                        LastKeepAlive = (DateTime)reader["LastKeepAlive"],
                        AssignedResources = reader["Resources"]
                            .ToString()
                            .Split(
                                new[]
                                {
                                    ',',
                                },
                                StringSplitOptions.RemoveEmptyEntries
                            )
                            .ToList(),
                    };

                    return client;
                }
            }
        }

        throw new ClientNotFoundException(clientId);
    }

    public async Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = @"UPDATE [RBR].[Clients]
   SET [ClientStatus] = @ClientStatus
WHERE ClientId = @ClientId";

            command.Parameters.Add("@ClientStatus", SqlDbType.TinyInt).Value = clientStatus;
            command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
            await command.ExecuteNonQueryAsync();
        }
    }

    public Task<ModifyClientResult> StartActivityAsync(
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    ) => StartActivityAsync(null, Guid.Empty, fencingToken, clientStartRequests);

    public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
        StopActivityAsync(null, Guid.Empty, fencingToken, clients);

    public async Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            using var command = conn.CreateCommand();
            command.CommandText = $"SELECT CASE WHEN {LeaseGuard} THEN 1 ELSE 0 END";
            AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
            return (int)await command.ExecuteScalarAsync() == 1;
        }
    }

    // resourceGroup null: no lease guard (the public IClientService members)
    public async Task<ModifyClientResult> StartActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    )
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            // all clients are started or none: disposing the uncommitted transaction rolls back earlier updates
            using var transaction = conn.BeginTransaction();
            using var command = conn.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"UPDATE [RBR].[Clients]
   SET [CoordinatorStatus] = @CoordinatorStatus
      ,[Resources] = @Resources
      ,[FencingToken] = @FencingToken
WHERE ClientId = @ClientId
AND FencingToken <= @FencingToken" + (resourceGroup == null ? "" : $@"
AND {LeaseGuard}") + @"
SELECT @@ROWCOUNT";

            foreach (var request in clientStartRequests)
            {
                command.Parameters.Clear();
                command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value =
                    CoordinatorStatus.ResourcesGranted;
                command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = request.ClientId;
                command.Parameters.Add("@Resources", SqlDbType.VarChar, -1).Value =
                    string.Join(",", request.AssignedResources);
                AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
                var result = (int)await command.ExecuteScalarAsync();
                if (result == 0)
                {
                    return ModifyClientResult.FencingTokenViolation;
                }
            }

            transaction.Commit();
            return ModifyClientResult.Ok;
        }
    }

    // resourceGroup null: no lease guard (the public IClientService members)
    public async Task<ModifyClientResult> StopActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<Client> clients
    )
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            var command = conn.CreateCommand();
            command.CommandText = GetSetStatusQuery(clients, resourceGroup != null);
            command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.StopActivity;
            AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);

            for (var i = 0; i < clients.Count; i++)
            {
                command.Parameters.Add($"@Client{i}", SqlDbType.UniqueIdentifier).Value = clients[i].ClientId;
            }

            var rowsUpdated = (int)await command.ExecuteScalarAsync();

            if (rowsUpdated != clients.Count)
            {
                return ModifyClientResult.FencingTokenViolation;
            }

            return ModifyClientResult.Ok;
        }
    }

    public async Task<int> DeleteExpiredClientsAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        TimeSpan retention
    )
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            using var command = conn.CreateCommand();
            // age is judged by the database clock; the EXISTS clause is the fencing guard (ADR-0014)
            command.CommandText = $@"DELETE FROM [RBR].[Clients]
WHERE ResourceGroup = @ResourceGroup
  AND ClientId <> @CoordinatorId
  AND LastKeepAlive < DATEADD(SECOND, -@RetentionSeconds, GETUTCDATE())
  AND {LeaseGuard}
SELECT @@ROWCOUNT";
            AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
            command.Parameters.Add("@RetentionSeconds", SqlDbType.Int).Value = (int)Math.Min(
                int.MaxValue,
                Math.Ceiling(retention.TotalSeconds)
            );
            return (int)await command.ExecuteScalarAsync();
        }
    }

    public async Task DeleteClientAsync(Guid clientId)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            using var command = conn.CreateCommand();
            command.CommandText = "DELETE FROM [RBR].[Clients] WHERE ClientId = @ClientId";
            command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task ResetClientAsync(Guid clientId)
    {
        using (var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString))
        {
            using var command = conn.CreateCommand();
            command.CommandText = @"UPDATE [RBR].[Clients]
   SET [ClientStatus] = @ClientStatus
      ,[CoordinatorStatus] = @CoordinatorStatus
      ,[Resources] = ''
WHERE ClientId = @ClientId";
            command.Parameters.Add("@ClientStatus", SqlDbType.TinyInt).Value = ClientStatus.Waiting;
            command.Parameters.Add("@CoordinatorStatus", SqlDbType.TinyInt).Value = CoordinatorStatus.StopActivity;
            command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
            await command.ExecuteNonQueryAsync();
        }
    }

    // @FencingToken always; the lease guard parameters only when resourceGroup is given
    private static void AddLeaseGuardParameters(
        SqlCommand command,
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken
    )
    {
        command.Parameters.Add("@FencingToken", SqlDbType.Int).Value = fencingToken;
        if (resourceGroup != null)
        {
            command.Parameters.Add("@ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("@CoordinatorId", SqlDbType.UniqueIdentifier).Value = coordinatorClientId;
        }
    }

    private string GetSetStatusQuery(List<Client> clients, bool leaseGuard)
    {
        StringBuilder sb = new();
        sb.Append(
            @"UPDATE [RBR].[Clients]
   SET [CoordinatorStatus] = @CoordinatorStatus
      ,[Resources] = ''
      ,[FencingToken] = @FencingToken
WHERE ClientId IN ("
        );

        for (var i = 0; i < clients.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }

            sb.Append($"@Client{i}");
        }

        sb.Append(
            @")
AND FencingToken <= @FencingToken"
        );
        if (leaseGuard)
        {
            sb.Append($@"
AND {LeaseGuard}");
        }

        sb.Append(@"
SELECT @@ROWCOUNT");

        return sb.ToString();
    }
}
