using Rebalancer.Core;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Clients;

internal class ClientService : IClientService, IFencedClientService
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

    private RedisKey ClientsKey =>
        clientsKey ?? throw new RebalancerException("No client has registered with this service");

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

    // ADR-0037: changes the keep-alive only, in a script, so it never conflicts with the coordinator's writes
    public async Task<Client> KeepAliveAsync(Guid clientId)
    {
        var key = clientsKey ?? throw new ClientNotFoundException(clientId);
        var now = await RedisHash.GetServerTimeAsync(cache);
        var record = await RedisScripts.KeepAliveAsync(cache, key, RedisHash.Encode(clientId), now);
        return record.IsNull ? throw new ClientNotFoundException(clientId) : RedisHash.Decode<Client>(record);
    }

    public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus) =>
        RedisScripts.SetStatusAsync(cache, ClientsKey, RedisHash.Encode(clientId), (int)clientStatus);

    public Task<ModifyClientResult> StartActivityAsync(
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    ) =>
        StartActivityAsync(null, Guid.Empty, fencingToken, clientStartRequests);

    public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
        StopActivityAsync(null, Guid.Empty, fencingToken, clients);

    public async Task DeleteClientAsync(Guid clientId) =>
        await cache.HashDeleteAsync(ClientsKey, RedisHash.Encode(clientId));

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

        if ((await GetLeaseIfHeldAsync(resourceGroup, coordinatorClientId, fencingToken)).IsNull)
        {
            return 0;
        }

        var now = await RedisHash.GetServerTimeAsync(cache);
        var expired = (await cache.HashGetAllAsync(groupKey)).Where(x =>
                {
                    var client = RedisHash.Decode<Client>(x.Value);
                    return client.ClientId != coordinatorClientId && now - client.LastKeepAlive > retention;
                }
            )
            .ToArray();
        if (!expired.Any())
        {
            return 0;
        }

        // only entries still unchanged (not kept alive meanwhile), and only while this coordinator holds the same lease
        // (fencing guard, ADR-0014); a script, so other writes cannot make it fail (ADR-0037)
        return await RedisScripts.DeleteUnchangedWhileHoldingLeaseAsync(
            cache,
            groupKey,
            RedisHash.LeaseKey(resourceGroup),
            RedisHash.Encode(resourceGroup),
            coordinatorClientId,
            fencingToken,
            expired
        );
    }

    public async Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken) =>
        !(await GetLeaseIfHeldAsync(resourceGroup, coordinatorClientId, fencingToken)).IsNull;

    // resourceGroup null: no lease guard (the public IClientService members)
    public Task<ModifyClientResult> StartActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    )
    {
        var requests = clientStartRequests.ToDictionary(x => x.ClientId);
        return ModifyClientsAsync(
            resourceGroup,
            coordinatorClientId,
            fencingToken,
            requests.Keys.ToList(),
            client =>
            {
                client.CoordinatorStatus = CoordinatorStatus.ResourcesGranted;
                client.AssignedResources = requests[client.ClientId].AssignedResources;
            }
        );
    }

    // resourceGroup null: no lease guard (the public IClientService members)
    public Task<ModifyClientResult> StopActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<Client> clients
    ) =>
        ModifyClientsAsync(
            resourceGroup,
            coordinatorClientId,
            fencingToken,
            clients.Select(x => x.ClientId).ToList(),
            client =>
            {
                client.CoordinatorStatus = CoordinatorStatus.StopActivity;
                client.AssignedResources = new List<string>();
            }
        );

    // PRB-0005: as a new registration would write it, keeping the record's fencing token and keep-alive
    public async Task ResetClientAsync(Guid clientId)
    {
        var field = RedisHash.Encode(clientId);
        var current = await cache.HashGetAsync(ClientsKey, field);
        if (current.IsNull)
        {
            return;
        }

        var client = RedisHash.Decode<Client>(current);
        client.ClientStatus = ClientStatus.Waiting;
        client.CoordinatorStatus = CoordinatorStatus.StopActivity;
        client.AssignedResources = new List<string>();
        await RedisScripts.WriteGuardedRecordAsync(
            cache,
            ClientsKey,
            ClientsKey,
            field,
            null,
            Guid.Empty,
            int.MaxValue, // no fencing check: the node resets its own record
            client,
            RedisScripts.KeptFields.FencingToken
        );
    }

    /// <summary>
    ///     Applies <paramref name="modify" /> to the clients one at a time, in the given order (ADR-0036; the coordinator
    ///     puts its own record first): each record is written only if it
    ///     exists and has not seen a newer fencing token (and, given <paramref name="resourceGroup" />, the lease still
    ///     names <paramref name="coordinatorClientId" /> with that token). The first record that fails stops the loop;
    ///     the records written before it stay written.
    /// </summary>
    private async Task<ModifyClientResult> ModifyClientsAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<Guid> clientIds,
        Action<Client> modify
    )
    {
        foreach (var clientId in clientIds)
        {
            var result = await ModifyClientAsync(resourceGroup, coordinatorClientId, fencingToken, clientId, modify);
            if (result != ModifyClientResult.Ok)
            {
                return result;
            }
        }

        return ModifyClientResult.Ok;
    }

    // one record (ADR-0036), in a script (ADR-0037): written only if it exists and has not seen a newer token, and
    // (given resourceGroup) while the lease names this coordinator and token; the node's keep-alive and status are kept,
    // so neither they nor another node's write can make it fail
    private async Task<ModifyClientResult> ModifyClientAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        Guid clientId,
        Action<Client> modify
    )
    {
        var field = RedisHash.Encode(clientId);
        var current = await cache.HashGetAsync(ClientsKey, field);
        if (current.IsNull)
        {
            return ModifyClientResult.FencingTokenViolation;
        }

        var client = RedisHash.Decode<Client>(current);
        if (client.FencingToken > fencingToken)
        {
            return ModifyClientResult.FencingTokenViolation;
        }

        modify(client);
        client.FencingToken = fencingToken;
        var written = await RedisScripts.WriteGuardedRecordAsync(
            cache,
            ClientsKey,
            resourceGroup == null ? ClientsKey : RedisHash.LeaseKey(resourceGroup),
            field,
            resourceGroup == null ? null : (string)RedisHash.Encode(resourceGroup),
            coordinatorClientId,
            fencingToken,
            client,
            RedisScripts.KeptFields.ClientStatus
        );
        return written ? ModifyClientResult.Ok : ModifyClientResult.FencingTokenViolation;
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
                transaction.AddCondition(
                    Condition.HashEqual(RedisHash.LeaseKey(resourceGroup), RedisHash.Encode(resourceGroup), lease)
                );
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
        return group.CoordinatorId == coordinatorClientId && group.FencingToken == fencingToken ? lease
            : RedisValue.Null;
    }
}
