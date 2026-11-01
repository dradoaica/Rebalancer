using Npgsql;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.PostgreSql.Connections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.PostgreSql.Clients;

internal class ClientService : IClientService, IFencedClientService
{
    // the fencing guard of a coordinator write (ADR-0014, ADR-0026): the lease still names this coordinator and token
    private const string LeaseGuard = @"EXISTS (SELECT 1 FROM rbr.resource_groups
                WHERE resource_group = @ResourceGroup
                  AND coordinator_id = @CoordinatorId
                  AND fencing_token = @FencingToken)";

    private readonly string connectionString;

    public ClientService(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task CreateClientAsync(string resourceGroup, Guid clientId)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = @"INSERT INTO rbr.clients
       (client_id, resource_group, last_keep_alive, client_status, coordinator_status, resources, fencing_token)
VALUES (@ClientId, @ResourceGroup, clock_timestamp(), @ClientStatus, @CoordinatorStatus, '', 1)";
        command.Parameters.AddWithValue("ClientId", clientId);
        command.Parameters.AddWithValue("ResourceGroup", resourceGroup);
        command.Parameters.AddWithValue("ClientStatus", (short)ClientStatus.Waiting);
        command.Parameters.AddWithValue("CoordinatorStatus", (short)CoordinatorStatus.StopActivity);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<Client>> GetActiveClientsAsync(string resourceGroup)
    {
        List<Client> clients = new();
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = @"SELECT client_id, last_keep_alive, client_status, coordinator_status, fencing_token,
       clock_timestamp() AS time_now
  FROM rbr.clients
 WHERE resource_group = @ResourceGroup
   AND (client_status = 0 OR client_status = 1)";
        command.Parameters.AddWithValue("ResourceGroup", resourceGroup);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            clients.Add(
                new Client
                {
                    ClientId = reader.GetGuid(0),
                    LastKeepAlive = reader.GetDateTime(1),
                    ClientStatus = (ClientStatus)reader.GetInt16(2),
                    CoordinatorStatus = (CoordinatorStatus)reader.GetInt16(3),
                    FencingToken = reader.GetInt32(4),
                    TimeNow = reader.GetDateTime(5),
                }
            );
        }

        return clients;
    }

    public async Task<Client> KeepAliveAsync(Guid clientId)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = @"UPDATE rbr.clients
   SET last_keep_alive = clock_timestamp()
 WHERE client_id = @ClientId
RETURNING client_status, coordinator_status, last_keep_alive, resources, fencing_token";
        command.Parameters.AddWithValue("ClientId", clientId);
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Client
            {
                ClientId = clientId,
                ClientStatus = (ClientStatus)reader.GetInt16(0),
                CoordinatorStatus = (CoordinatorStatus)reader.GetInt16(1),
                LastKeepAlive = reader.GetDateTime(2),
                // PRB-0007: a delayed start goes on only for the same grant (resources and token)
                FencingToken = reader.GetInt32(4),
                AssignedResources = reader.GetString(3)
                    .Split(
                        new[]
                        {
                            ',',
                        },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                    .ToList(),
            };
        }

        throw new ClientNotFoundException(clientId);
    }

    public async Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = "UPDATE rbr.clients SET client_status = @ClientStatus WHERE client_id = @ClientId";
        command.Parameters.AddWithValue("ClientStatus", (short)clientStatus);
        command.Parameters.AddWithValue("ClientId", clientId);
        await command.ExecuteNonQueryAsync();
    }

    public Task<ModifyClientResult> StartActivityAsync(
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    ) => StartActivityAsync(null, Guid.Empty, fencingToken, clientStartRequests);

    public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
        StopActivityAsync(null, Guid.Empty, fencingToken, clients);

    public async Task<int> DeleteExpiredClientsAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        TimeSpan retention
    )
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        // age is judged by the database clock; the EXISTS clause is the fencing guard (ADR-0014)
        command.CommandText = $@"DELETE FROM rbr.clients
 WHERE resource_group = @ResourceGroup
   AND client_id <> @CoordinatorId
   AND last_keep_alive < clock_timestamp() - make_interval(secs => @RetentionSeconds)
   AND {LeaseGuard}";
        AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
        command.Parameters.AddWithValue("RetentionSeconds", Math.Ceiling(retention.TotalSeconds));
        return await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteClientAsync(Guid clientId)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = "DELETE FROM rbr.clients WHERE client_id = @ClientId";
        command.Parameters.AddWithValue("ClientId", clientId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = $"SELECT {LeaseGuard}";
        AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
        return (bool)await command.ExecuteScalarAsync();
    }

    // resourceGroup null: no lease guard (the public IClientService members)
    public async Task<ModifyClientResult> StartActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    )
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        // all clients are started or none: disposing the uncommitted transaction rolls back earlier updates
        await using var transaction = await conn.BeginTransactionAsync();
        if (resourceGroup != null)
        {
            // the updated rows stay locked until the commit: bound a stall as in the lease transaction (TM-0004 D2)
            await using var bound = new NpgsqlCommand(StallBound.SetTimeouts, conn, transaction);
            bound.Parameters.AddWithValue("ResourceGroup", resourceGroup);
            await bound.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            @"UPDATE rbr.clients
   SET coordinator_status = @CoordinatorStatus,
       resources = @Resources,
       fencing_token = @FencingToken
 WHERE client_id = @ClientId
   AND fencing_token <= @FencingToken" +
            (resourceGroup == null ? "" : $@"
   AND {LeaseGuard}"),
            conn,
            transaction
        );

        foreach (var request in clientStartRequests)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("CoordinatorStatus", (short)CoordinatorStatus.ResourcesGranted);
            command.Parameters.AddWithValue("ClientId", request.ClientId);
            command.Parameters.AddWithValue("Resources", string.Join(",", request.AssignedResources));
            AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);
            if (await command.ExecuteNonQueryAsync() == 0)
            {
                return ModifyClientResult.FencingTokenViolation;
            }
        }

        await transaction.CommitAsync();
        return ModifyClientResult.Ok;
    }

    // resourceGroup null: no lease guard (the public IClientService members)
    public async Task<ModifyClientResult> StopActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<Client> clients
    )
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        // TM-0004 T2: the client ids are one array parameter, so no statement text is built from data
        command.CommandText = @"UPDATE rbr.clients
   SET coordinator_status = @CoordinatorStatus,
       resources = '',
       fencing_token = @FencingToken
 WHERE client_id = ANY(@ClientIds)
   AND fencing_token <= @FencingToken" +
                              (resourceGroup == null ? "" : $@"
   AND {LeaseGuard}");
        command.Parameters.AddWithValue("CoordinatorStatus", (short)CoordinatorStatus.StopActivity);
        command.Parameters.AddWithValue("ClientIds", clients.Select(x => x.ClientId).ToArray());
        AddLeaseGuardParameters(command, resourceGroup, coordinatorClientId, fencingToken);

        var rowsUpdated = await command.ExecuteNonQueryAsync();
        return rowsUpdated != clients.Count ? ModifyClientResult.FencingTokenViolation : ModifyClientResult.Ok;
    }

    public async Task ResetClientAsync(Guid clientId)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = @"UPDATE rbr.clients
   SET client_status = @ClientStatus,
       coordinator_status = @CoordinatorStatus,
       resources = ''
 WHERE client_id = @ClientId";
        command.Parameters.AddWithValue("ClientStatus", (short)ClientStatus.Waiting);
        command.Parameters.AddWithValue("CoordinatorStatus", (short)CoordinatorStatus.StopActivity);
        command.Parameters.AddWithValue("ClientId", clientId);
        await command.ExecuteNonQueryAsync();
    }

    // @FencingToken always; the lease guard parameters only when resourceGroup is given
    private static void AddLeaseGuardParameters(
        NpgsqlCommand command,
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken
    )
    {
        command.Parameters.AddWithValue("FencingToken", fencingToken);
        if (resourceGroup != null)
        {
            command.Parameters.AddWithValue("ResourceGroup", resourceGroup);
            command.Parameters.AddWithValue("CoordinatorId", coordinatorClientId);
        }
    }
}
