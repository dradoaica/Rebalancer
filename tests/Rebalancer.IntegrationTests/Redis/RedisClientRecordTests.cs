using Rebalancer.Core;
using Rebalancer.Redis;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using StackExchange.Redis.DataTypes.Collections;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. ADR-0014 (stale client records expire, guarded by the coordinator's fencing token) and
///     ADR-0015 (one client hash per resource group; 2.x entries move over, groups are isolated) against a real Redis.
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
        SeedLease(db, group, coordinator, 3);

        var coordinatorService = new ClientService(db);
        await coordinatorService.CreateClientAsync(group, coordinator);
        var live = Guid.NewGuid();
        await new ClientService(db).CreateClientAsync(group, live);
        var stale = Guid.NewGuid();
        await SeedClientAsync(db, RedisHash.GroupKey("Clients", group), stale, group, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(
            0,
            await coordinatorService.DeleteExpiredClientsAsync(group, coordinator, 2, Retention)
        ); // stale token
        Assert.Equal(1, await coordinatorService.DeleteExpiredClientsAsync(group, coordinator, 3, Retention));

        var remaining =
            (await db.HashKeysAsync(RedisHash.GroupKey("Clients", group))).Select(x => RedisHash.Decode<Guid>(x));
        Assert.Equal(
            new[]
            {
                coordinator, live,
            }.OrderBy(x => x),
            remaining.OrderBy(x => x)
        );
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
        SeedLease(db, "orders", coordinator, 1);

        // 2.x layout: every group's clients in one hash, written by the same library 2.x used
        var legacy = new RedisDictionary<Guid, Client>(db, "RBR:Clients");
        var orders1 = Guid.NewGuid();
        var orders2 = Guid.NewGuid();
        var invoices = Guid.NewGuid();
        foreach (var (id, group) in new[]
                 {
                     (orders1, "orders"), (orders2, "orders"), (invoices, "invoices"),
                 })
        {
            legacy[id] = new Client
            {
                ClientId = id,
                ResourceGroup = group,
                LastKeepAlive = DateTime.UtcNow,
            };
        }

        var service = new ClientService(db);
        await service.CreateClientAsync("orders", coordinator);
        await service.DeleteExpiredClientsAsync("orders", coordinator, 1, Retention);

        var orders = (await db.HashKeysAsync("RBR:Clients:orders")).Select(x => RedisHash.Decode<Guid>(x)).ToHashSet();
        Assert.Superset(
            new HashSet<Guid>
            {
                orders1,
                orders2,
                coordinator,
            },
            orders
        );
        Assert.Equal(
            new[]
            {
                invoices,
            },
            legacy.Keys
        ); // the other group's legacy entry is untouched
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
        SeedLease(adminDb, group, coordinator, 1);
        var stale = Guid.NewGuid();
        await SeedClientAsync(
            adminDb,
            RedisHash.GroupKey("Clients", group),
            stale,
            group,
            DateTime.UtcNow.AddHours(-1)
        );

        // the documented minimal per-group user: no access to the 2.x hash Dictionary:RBR:Clients
        await CreateMinimalGroupUserAsync(adminDb, "orders");
        var options = ConfigurationOptions.Parse(redis.GetConnectionString());
        options.User = "orders-user";
        options.Password = "orders-pass";
        options.AbortOnConnectFail = false;
        using var limited = await ConnectionMultiplexer.ConnectAsync(options);
        var service = new ClientService(limited.GetDatabase());
        await service.CreateClientAsync(group, coordinator);

        Assert.Equal(1, await service.DeleteExpiredClientsAsync(group, coordinator, 1, Retention));
        await Assert.ThrowsAsync<RedisServerException>(() =>
            limited.GetDatabase().HashLengthAsync("Dictionary:RBR:Clients")
        );
    }

    [SkippableFact]
    public async Task ARebalancerClient_WorksEndToEnd_WithTheMinimalPerGroupUser()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        using var admin = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var adminDb = admin.GetDatabase();
        // provisioned exactly as docs/release-notes/migrating-to-4.0.md shows (4.0 layout, ADR-0020)
        await adminDb.ExecuteAsync(
            "HSET",
            "RBR:Lease:orders",
            "\"orders\"",
            "{\"Name\":\"orders\",\"LeaseExpirySeconds\":5,\"HeartbeatSeconds\":1,\"FencingToken\":0}"
        );
        await adminDb.ExecuteAsync(
            "RPUSH",
            "RBR:Resources:orders",
            "{\"ResourceGroup\":\"orders\",\"ResourceName\":\"r1\"}"
        );
        await CreateMinimalGroupUserAsync(adminDb, "orders");

        var connectionString =
            $"{redis.GetConnectionString()},user=orders-user,password=orders-pass,abortConnect=false";
        using var client = new RebalancerClient(new RedisProvider(connectionString));
        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (client.GetCurrentState() != ClientState.Assigned && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            Assert.Equal(
                new[]
                {
                    "r1",
                },
                client.GetAssignedResources().Resources
            );
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(15));
        }

        // a clean stop deletes the node's own record through the restricted user (ADR-0014)
        Assert.Equal(0, await adminDb.HashLengthAsync(RedisHash.GroupKey("Clients", "orders")));

        // and the user has no access to any shared key (ADR-0020 confirmation)
        using var limited = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await Assert.ThrowsAsync<RedisServerException>(() =>
            limited.GetDatabase().HashLengthAsync("Dictionary:RBR:ResourceGroups")
        );
        await Assert.ThrowsAsync<RedisServerException>(() => limited.GetDatabase().ListLengthAsync("List:RBR:Resources")
        );
    }

    // The documented minimal Redis ACL user for one resource group in 4.0, with no shared keys (ADR-0020; command set
    // verified 2026-10-18 against redis:7.0 and
    // StackExchange.Redis 3.4.5): data and transaction commands, TIME for the server clock, and ECHO, which the
    // client library's connection handshake requires. Its other handshake commands (CLIENT, CLUSTER, CONFIG GET,
    // INFO, SELECT, SUBSCRIBE) are denied but tolerated by the library.
    private static Task CreateMinimalGroupUserAsync(IDatabase adminDb, string group) =>
        adminDb.ExecuteAsync(
            "ACL",
            "SETUSER",
            $"{group}-user",
            "on",
            $">{group}-pass",
            "resetchannels",
            $"~RBR:Clients:{RedisHash.EscapeKeySegment(group)}",
            $"~RBR:Lease:{RedisHash.EscapeKeySegment(group)}",
            $"~RBR:Resources:{RedisHash.EscapeKeySegment(group)}",
            "+@read",
            "+@write",
            "+@transaction",
            "+time",
            "+echo"
        );

    [Fact]
    public void GroupNamesAreEscapedInKeys() =>
        Assert.Equal("RBR:Clients:a%2Ab%20c%C3%A9", (string)RedisHash.GroupKey("Clients", "a*b cé")!);

    // ADR-0026 against a real Redis: coordinator writes change nothing unless the lease names that coordinator with that
    // token; PRB-0005: a reset record looks like a new registration and keeps its token
    [SkippableFact]
    public async Task CoordinatorWritesNeedTheLease_AndAResetKeepsTheFencingToken()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        using var mux = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var db = mux.GetDatabase();
        const string group = "orders";
        var coordinator = Guid.NewGuid();
        SeedLease(db, group, coordinator, 3);
        var service = new ClientService(db);
        IFencedClientService fenced = service;
        var follower = Guid.NewGuid();
        await service.CreateClientAsync(group, follower);
        var targets = (await service.GetActiveClientsAsync(group)).Where(x => x.ClientId == follower).ToList();
        List<ClientStartRequest> grant = [new() { ClientId = follower, AssignedResources = ["r1",], },];

        Assert.True(await fenced.HoldsLeaseAsync(group, coordinator, 3));
        Assert.False(await fenced.HoldsLeaseAsync(group, coordinator, 2));
        Assert.False(await fenced.HoldsLeaseAsync(group, Guid.NewGuid(), 3));

        Assert.Equal(
            ModifyClientResult.FencingTokenViolation,
            await fenced.StartActivityAsync(group, coordinator, 2, grant)
        );
        Assert.Equal(CoordinatorStatus.StopActivity, (await service.KeepAliveAsync(follower)).CoordinatorStatus);
        Assert.Equal(ModifyClientResult.Ok, await fenced.StartActivityAsync(group, coordinator, 3, grant));
        var granted = await service.KeepAliveAsync(follower);
        Assert.Equal(CoordinatorStatus.ResourcesGranted, granted.CoordinatorStatus);
        Assert.Equal(["r1",], granted.AssignedResources);

        Assert.Equal(
            ModifyClientResult.FencingTokenViolation,
            await fenced.StopActivityAsync(group, Guid.NewGuid(), 3, targets)
        );
        Assert.Equal(CoordinatorStatus.ResourcesGranted, (await service.KeepAliveAsync(follower)).CoordinatorStatus);
        Assert.Equal(ModifyClientResult.Ok, await fenced.StopActivityAsync(group, coordinator, 3, targets));
        Assert.Equal(CoordinatorStatus.StopActivity, (await service.KeepAliveAsync(follower)).CoordinatorStatus);

        await fenced.StartActivityAsync(group, coordinator, 3, grant);
        await service.SetClientStatusAsync(follower, ClientStatus.Active);
        await fenced.ResetClientAsync(follower);
        var reset = await service.KeepAliveAsync(follower);
        Assert.Equal(ClientStatus.Waiting, reset.ClientStatus);
        Assert.Equal(CoordinatorStatus.StopActivity, reset.CoordinatorStatus);
        Assert.Equal(3, reset.FencingToken);
        Assert.Empty(reset.AssignedResources);
    }

    private static void SeedLease(IDatabase db, string group, Guid coordinator, int fencingToken) =>
        db.HashSet(
            RedisHash.LeaseKey(group),
            RedisHash.Encode(group),
            RedisHash.Encode(
                new ResourceGroup
                {
                    Name = group,
                    CoordinatorId = coordinator,
                    FencingToken = fencingToken,
                    LastCoordinatorRenewal = DateTime.UtcNow,
                    LeaseExpirySeconds = 60,
                    HeartbeatSeconds = 25,
                }
            )
        );

    private static Task SeedClientAsync(IDatabase db, RedisKey key, Guid id, string group, DateTime lastKeepAlive) =>
        db.HashSetAsync(
            key,
            RedisHash.Encode(id),
            RedisHash.Encode(
                new Client
                {
                    ClientId = id,
                    ResourceGroup = group,
                    LastKeepAlive = lastKeepAlive,
                }
            )
        );
}
