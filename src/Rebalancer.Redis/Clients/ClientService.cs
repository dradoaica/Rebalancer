using Rebalancer.Core;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Clients;

internal class ClientService : IClientService
{
    private const int MaxCompareAndSetAttempts = 10;
    private static readonly RedisKey ClientsKey = RedisHash.DictionaryKey("Clients");
    private readonly IDatabase cache;

    public ClientService(IDatabase cache)
    {
        this.cache = cache;
    }

    public async Task CreateClientAsync(string resourceGroup, Guid clientId)
    {
        var now = await RedisHash.GetServerTimeAsync(cache);
        await cache.HashSetAsync(
            ClientsKey,
            RedisHash.Encode(clientId),
            RedisHash.Encode(
                new Client
                {
                    ClientId = clientId,
                    ResourceGroup = resourceGroup,
                    LastKeepAlive = now,
                    ClientStatus = ClientStatus.Waiting,
                    CoordinatorStatus = CoordinatorStatus.StopActivity,
                    FencingToken = 1,
                }
            )
        );
    }

    public async Task<List<Client>> GetActiveClientsAsync(string resourceGroup)
    {
        var values = await cache.HashValuesAsync(ClientsKey);
        var now = await RedisHash.GetServerTimeAsync(cache);
        var clients = values.Select(RedisHash.Decode<Client>)
            .Where(x => x.ResourceGroup == resourceGroup &&
                        (x.ClientStatus == ClientStatus.Waiting || x.ClientStatus == ClientStatus.Active)
            )
            .ToList();
        clients.ForEach(x => x.TimeNow = now);
        return clients;
    }

    public Task<Client> KeepAliveAsync(Guid clientId) =>
        RedisHash.CompareAndSetAsync<Client, Client>(
            cache,
            ClientsKey,
            RedisHash.Encode(clientId),
            (client, now) =>
            {
                if (client == null)
                {
                    throw new RebalancerException($"No client exists with id {clientId}");
                }

                client.LastKeepAlive = now;
                return (client, client);
            }
        );

    public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus) =>
        RedisHash.CompareAndSetAsync<Client, bool>(
            cache,
            ClientsKey,
            RedisHash.Encode(clientId),
            (client, _) =>
            {
                if (client == null)
                {
                    return (null, false);
                }

                client.ClientStatus = clientStatus;
                return (client, true);
            }
        );

    public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> clientStartRequests)
    {
        var requests = clientStartRequests.ToDictionary(x => x.ClientId);
        return ModifyClientsAsync(
            fencingToken,
            requests.Keys.ToList(),
            client =>
            {
                client.CoordinatorStatus = CoordinatorStatus.ResourcesGranted;
                client.AssignedResources = requests[client.ClientId].AssignedResources;
            }
        );
    }

    public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
        ModifyClientsAsync(
            fencingToken,
            clients.Select(x => x.ClientId).ToList(),
            client =>
            {
                client.CoordinatorStatus = CoordinatorStatus.StopActivity;
                client.AssignedResources = new List<string>();
            }
        );

    /// <summary>
    ///     Applies <paramref name="modify" /> to all clients atomically: either every client exists and has not seen a
    ///     newer fencing token and all are written, or nothing is written.
    /// </summary>
    private async Task<ModifyClientResult> ModifyClientsAsync(
        int fencingToken,
        List<Guid> clientIds,
        Action<Client> modify
    )
    {
        if (!clientIds.Any())
        {
            return ModifyClientResult.Ok;
        }

        var fields = clientIds.Select(x => RedisHash.Encode(x)).ToArray();
        for (var attempt = 0; attempt < MaxCompareAndSetAttempts; attempt++)
        {
            var current = await cache.HashGetAsync(ClientsKey, fields);
            if (current.Any(x => x.IsNull))
            {
                return ModifyClientResult.FencingTokenViolation;
            }

            var clients = current.Select(RedisHash.Decode<Client>).ToList();
            if (clients.Any(x => x.FencingToken > fencingToken))
            {
                return ModifyClientResult.FencingTokenViolation;
            }

            var transaction = cache.CreateTransaction();
            for (var i = 0; i < fields.Length; i++)
            {
                transaction.AddCondition(Condition.HashEqual(ClientsKey, fields[i], current[i]));
                modify(clients[i]);
                clients[i].FencingToken = fencingToken;
                _ = transaction.HashSetAsync(ClientsKey, fields[i], RedisHash.Encode(clients[i]));
            }

            if (await transaction.ExecuteAsync())
            {
                return ModifyClientResult.Ok;
            }
        }

        throw new RebalancerException("Could not modify clients due to concurrent modifications");
    }
}
