using Rebalancer.Redis;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using StackExchange.Redis.DataTypes.Collections;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. ADR-0014 (stale client records expire, guarded by the coordinator's fencing token) and ADR-0015
///     (one client hash per resource group; 2.x entries move over, groups are isolated) against a real Redis.
/// </summary>
public sealed class RedisClientRecordTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

    [SkippableFact]
    public async Task ExpiredRecordsAreDeleted_OnlyWhileTheCoordinatorHoldsTheLease()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        using var mux = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var db = mux.GetDatabase();
        const string group = "orders";
        var coordinator = Guid.NewGuid();
        SeedLease(db, group, coordinator, fencingToken: 3);

        var coordinatorService = new ClientService(db);
        await coordinatorService.CreateClientAsync(group, coordinator);
        var live = Guid.NewGuid();
        await new ClientService(db).CreateClientAsync(group, live);
        var stale = Guid.NewGuid();
        await SeedClientAsync(db, RedisHash.GroupKey("Clients", group), stale, group, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(0, await coordinatorService.DeleteExpiredClientsAsync(group, coordinator, 2, Retention)); // stale token
        Assert.Equal(1, await coordinatorService.DeleteExpiredClientsAsync(group, coordinator, 3, Retention));

        var remaining = (await db.HashKeysAsync(RedisHash.GroupKey("Clients", group))).Select(x => RedisHash.Decode<Guid>(x));
        Assert.Equal(new[] { coordinator, live }.OrderBy(x => x), remaining.OrderBy(x => x));
    }

    [SkippableFact]
    public async Task LegacyEntriesOfTheGroupMoveToItsOwnHash_AndOtherGroupsAreUntouched()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        using var mux = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var db = mux.GetDatabase();
        var coordinator = Guid.NewGuid();
        SeedLease(db, "orders", coordinator, fencingToken: 1);

        // 2.x layout: every group's clients in one hash, written by the same library 2.x used
        var legacy = new RedisDictionary<Guid, Client>(db, "RBR:Clients");
        var orders1 = Guid.NewGuid();
        var orders2 = Guid.NewGuid();
        var invoices = Guid.NewGuid();
        foreach (var (id, group) in new[] { (orders1, "orders"), (orders2, "orders"), (invoices, "invoices") })
        {
            legacy[id] = new Client { ClientId = id, ResourceGroup = group, LastKeepAlive = DateTime.UtcNow, };
        }

        var service = new ClientService(db);
        await service.CreateClientAsync("orders", coordinator);
        await service.DeleteExpiredClientsAsync("orders", coordinator, 1, Retention);

        var orders = (await db.HashKeysAsync("RBR:Clients:orders")).Select(x => RedisHash.Decode<Guid>(x)).ToHashSet();
        Assert.Superset(new HashSet<Guid> { orders1, orders2, coordinator }, orders);
        Assert.Equal(new[] { invoices }, legacy.Keys); // the other group's legacy entry is untouched
        Assert.False(await db.KeyExistsAsync("RBR:Clients:invoices"));
        Assert.Empty(await new ClientService(db).GetActiveClientsAsync("invoices")); // groups do not see each other
    }

    [SkippableFact]
    public async Task ExpiryWorks_ForARedisAclUserLimitedToItsGroup()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        using var admin = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var adminDb = admin.GetDatabase();
        const string group = "orders";
        var coordinator = Guid.NewGuid();
        SeedLease(adminDb, group, coordinator, fencingToken: 1);
        var stale = Guid.NewGuid();
        await SeedClientAsync(adminDb, RedisHash.GroupKey("Clients", group), stale, group, DateTime.UtcNow.AddHours(-1));

        // the per-group key patterns from the migration guide: no access to the 2.x hash Dictionary:RBR:Clients.
        // All commands are allowed because the client library's connection handshake needs more than data commands;
        // this test is about the key restriction.
        await adminDb.ExecuteAsync(
            "ACL", "SETUSER", "orders-user", "on", ">orders-pass", "allchannels", "+@all",
            "~RBR:Clients:orders", "~Dictionary:RBR:ResourceGroups", "~List:RBR:Resources"
        );
        var options = ConfigurationOptions.Parse(redis.GetConnectionString());
        options.User = "orders-user";
        options.Password = "orders-pass";
        using var limited = await ConnectionMultiplexer.ConnectAsync(options);
        var service = new ClientService(limited.GetDatabase());
        await service.CreateClientAsync(group, coordinator);

        Assert.Equal(1, await service.DeleteExpiredClientsAsync(group, coordinator, 1, Retention));
        await Assert.ThrowsAsync<RedisServerException>(() => limited.GetDatabase().HashLengthAsync("Dictionary:RBR:Clients"));
    }

    [Fact]
    public void GroupNamesAreEscapedInKeys() =>
        Assert.Equal("RBR:Clients:a%2Ab%20c%C3%A9", (string)RedisHash.GroupKey("Clients", "a*b cé")!);

    private static void SeedLease(IDatabase db, string group, Guid coordinator, int fencingToken) =>
        new RedisDictionary<string, ResourceGroup>(db, "RBR:ResourceGroups")[group] = new ResourceGroup
        {
            Name = group,
            CoordinatorId = coordinator,
            FencingToken = fencingToken,
            LastCoordinatorRenewal = DateTime.UtcNow,
            LeaseExpirySeconds = 60,
            HeartbeatSeconds = 25,
        };

    private static Task SeedClientAsync(IDatabase db, RedisKey key, Guid id, string group, DateTime lastKeepAlive) =>
        db.HashSetAsync(
            key,
            RedisHash.Encode(id),
            RedisHash.Encode(new Client { ClientId = id, ResourceGroup = group, LastKeepAlive = lastKeepAlive, })
        );
}
