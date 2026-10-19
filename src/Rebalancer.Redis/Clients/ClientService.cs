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
    // 2.x kept every group's clients in one hash; 3.0 uses one hash per group and moves old entries over (ADR-0015)
    private static readonly RedisKey LegacyClientsKey = RedisHash.DictionaryKey("Clients");
    private readonly IDatabase cache;

    // one provider, one resource group: set when this provider's client registers
    private RedisKey? clientsKey;
    private int? legacyMigrationDoneForFencingToken;

    public ClientService(IDatabase cache)
    {
        this.cache = cache;
    }

    private RedisKey ClientsKey => clientsKey ?? throw new RebalancerException("No client has registered with this service");

    public async Task CreateClientAsync(string resourceGroup, Guid clientId)
    {
        clientsKey = RedisHash.GroupKey("Clients", resourceGroup);
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
        var values = await cache.HashValuesAsync(RedisHash.GroupKey("Clients", resourceGroup));
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
            clientsKey ?? throw new ClientNotFoundException(clientId),
            RedisHash.Encode(clientId),
            (client, now) =>
            {
                if (client == null)
                {
                    throw new ClientNotFoundException(clientId);
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

    public async Task DeleteClientAsync(Guid clientId) => await cache.HashDeleteAsync(ClientsKey, RedisHash.Encode(clientId));

    public async Task<int> DeleteExpiredClientsAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        TimeSpan retention
    )
    {
        var groupKey = RedisHash.GroupKey("Clients", resourceGroup);
        try
        {
            await MigrateLegacyClientsOnceAsync(resourceGroup, groupKey, coordinatorClientId, fencingToken);
        }
        catch (RedisServerException)
        {
            // e.g. NOPERM: a Redis ACL user limited to this group's keys cannot read the 2.x hash. The migration is
            // optional (old entries belong to stopped processes); it must not prevent the expiry below.
            legacyMigrationDoneForFencingToken = fencingToken;
        }

        for (var attempt = 0; attempt < MaxCompareAndSetAttempts; attempt++)
        {
            var lease = await GetLeaseIfHeldAsync(resourceGroup, coordinatorClientId, fencingToken);
            if (lease.IsNull)
            {
                return 0;
            }

            var now = await RedisHash.GetServerTimeAsync(cache);
            var expired = (await cache.HashGetAllAsync(groupKey))
                .Where(x =>
                    {
                        var client = RedisHash.Decode<Client>(x.Value);
                        return client.ClientId != coordinatorClientId && now - client.LastKeepAlive > retention;
                    }
                )
                .ToList();
            if (!expired.Any())
            {
                return 0;
            }

            // all-or-nothing, and only while this coordinator still holds the same lease (fencing guard, ADR-0014)
            var transaction = cache.CreateTransaction();
            transaction.AddCondition(Condition.HashEqual(RedisHash.LeaseKey(resourceGroup), RedisHash.Encode(resourceGroup), lease));
            foreach (var entry in expired)
            {
                transaction.AddCondition(Condition.HashEqual(groupKey, entry.Name, entry.Value));
                _ = transaction.HashDeleteAsync(groupKey, entry.Name);
            }

            if (await transaction.ExecuteAsync())
            {
                return expired.Count;
            }
        }

        return 0; // best effort: the next heartbeat tries again
    }

    // ADR-0015: once per coordinator tenure, move this group's entries from the 2.x hash into the group's hash, guarded by
    // the lease. After an all-nodes-at-once upgrade these are records of stopped processes; ADR-0014 later expires them.
    private async Task MigrateLegacyClientsOnceAsync(
        string resourceGroup,
        RedisKey groupKey,
        Guid coordinatorClientId,
        int fencingToken
    )
    {
        if (legacyMigrationDoneForFencingToken == fencingToken)
        {
            return;
        }

        for (var attempt = 0; attempt < MaxCompareAndSetAttempts; attempt++)
        {
            var lease = await GetLeaseIfHeldAsync(resourceGroup, coordinatorClientId, fencingToken);
            if (lease.IsNull)
            {
                return;
            }

            var legacy = (await cache.HashGetAllAsync(LegacyClientsKey))
                .Where(x => RedisHash.Decode<Client>(x.Value).ResourceGroup == resourceGroup)
                .ToList();
            if (legacy.Any())
            {
                var transaction = cache.CreateTransaction();
                transaction.AddCondition(Condition.HashEqual(RedisHash.LeaseKey(resourceGroup), RedisHash.Encode(resourceGroup), lease));
                foreach (var entry in legacy)
                {
                    transaction.AddCondition(Condition.HashEqual(LegacyClientsKey, entry.Name, entry.Value));
                    _ = transaction.HashSetAsync(groupKey, entry.Name, entry.Value, When.NotExists);
                    _ = transaction.HashDeleteAsync(LegacyClientsKey, entry.Name);
                }

                if (!await transaction.ExecuteAsync())
                {
                    continue;
                }
            }

            legacyMigrationDoneForFencingToken = fencingToken;
            return;
        }
    }

    // the raw lease record if coordinatorClientId still holds the lease with fencingToken, otherwise RedisValue.Null
    private async Task<RedisValue> GetLeaseIfHeldAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken)
    {
        var lease = await cache.HashGetAsync(RedisHash.LeaseKey(resourceGroup), RedisHash.Encode(resourceGroup));
        if (lease.IsNull)
        {
            return RedisValue.Null;
        }

        var group = RedisHash.Decode<ResourceGroup>(lease);
        return group.CoordinatorId == coordinatorClientId && group.FencingToken == fencingToken ? lease : RedisValue.Null;
    }
}
