using Rebalancer.Core;
using Rebalancer.Redis;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. ADR-0020 confirmation: a group provisioned in the 3.x shared keys moves to its own keys on the
///     first 4.0 start, exactly once even when nodes start together, and other groups stay in the shared keys.
/// </summary>
public sealed class RedisPerGroupKeysTests
{
    private const string Group = "orders";
    private const string OtherGroup = "invoices";

    [SkippableFact]
    public async Task A3xGroup_MovesToItsOwnKeys_OnFirstStart_AndOtherGroupsStayShared()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        await RedisSeed.SeedResourceGroupAsync(connectionString, Group, ["r1", "r2"]);
        await RedisSeed.SeedResourceGroupAsync(connectionString, OtherGroup, ["i1"]);

        using var client = new RebalancerClient(new RedisProvider(connectionString));
        await client.StartAsync(Group, new ClientOptions());
        try
        {
            await WaitUntilAsync(() => client.GetAssignedResources().Resources?.Count == 2);
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(15));
        }

        using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var db = mux.GetDatabase();
        Assert.True(await db.HashExistsAsync(RedisHash.LeaseKey(Group), RedisHash.Encode(Group)));
        Assert.Equal(["r1", "r2"], await ResourceNamesAsync(db, RedisHash.ResourcesKey(Group)));
        Assert.False(await db.HashExistsAsync("Dictionary:RBR:ResourceGroups", RedisHash.Encode(Group)));
        Assert.True(await db.HashExistsAsync("Dictionary:RBR:ResourceGroups", RedisHash.Encode(OtherGroup)));
        Assert.Equal(["i1"], await ResourceNamesAsync(db, "List:RBR:Resources"));
    }

    [SkippableFact]
    public async Task NodesStartingTogether_MoveTheGroupOnlyOnce()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        await RedisSeed.SeedResourceGroupAsync(connectionString, Group, ["r1", "r2", "r3"]);

        var clients = Enumerable.Range(0, 4).Select(_ => new RebalancerClient(new RedisProvider(connectionString))).ToList();
        try
        {
            await Task.WhenAll(clients.Select(c => c.StartAsync(Group, new ClientOptions())));
        }
        finally
        {
            foreach (var c in clients)
            {
                await c.StopAsync(TimeSpan.FromSeconds(15));
                c.Dispose();
            }
        }

        using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var db = mux.GetDatabase();
        Assert.Equal(["r1", "r2", "r3"], await ResourceNamesAsync(db, RedisHash.ResourcesKey(Group)));
        Assert.Equal(0, await db.ListLengthAsync("List:RBR:Resources"));
    }

    // found in the 4.0 -> 3.2 rollback rehearsal (CR-0006 follow-up, 2026-10-19): a per-group list left from an earlier
    // 4.x run must not duplicate the resources when the group moves again
    [SkippableFact]
    public async Task MovingAgainAfterARollback_ReplacesALeftoverResourceList()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        await RedisSeed.SeedResourceGroupAsync(connectionString, Group, ["r1"]);
        using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var db = mux.GetDatabase();
        await db.ListRightPushAsync(RedisHash.ResourcesKey(Group), RedisHash.Encode(new Resource { ResourceGroup = Group, ResourceName = "r1", }));

        using var client = new RebalancerClient(new RedisProvider(connectionString));
        await client.StartAsync(Group, new ClientOptions());
        try
        {
            await WaitUntilAsync(() => client.GetAssignedResources().Resources?.Count > 0);
            Assert.Equal(["r1"], client.GetAssignedResources().Resources);
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(15));
        }

        Assert.Equal(["r1"], await ResourceNamesAsync(db, RedisHash.ResourcesKey(Group)));
    }

    [SkippableFact]
    public async Task AUserWithoutSharedKeyAccess_OnAGroupNotMovedYet_GetsAnErrorPointingToTheGuide()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        await RedisSeed.SeedResourceGroupAsync(redis.GetConnectionString(), Group, ["r1"]);
        using var admin = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        await admin.GetDatabase().ExecuteAsync(
            "ACL", "SETUSER", "orders-user", "on", ">orders-pass", "resetchannels",
            "~RBR:Lease:orders", "~RBR:Resources:orders", "~RBR:Clients:orders",
            "+@read", "+@write", "+@transaction", "+time", "+echo"
        );

        using var provider = new RedisProvider(
            $"{redis.GetConnectionString()},user=orders-user,password=orders-pass,abortConnect=false"
        );
        var error = await Assert.ThrowsAsync<RebalancerException>(() =>
            provider.StartAsync(Group, new OnChangeActions(), CancellationToken.None, new ClientOptions()));
        Assert.Contains("migrating-to-4.0.md", error.Message);
    }

    private static async Task<string[]> ResourceNamesAsync(IDatabase db, RedisKey key) =>
        (await db.ListRangeAsync(key)).Select(x => RedisHash.Decode<Resource>(x).ResourceName).Order().ToArray();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the client was never assigned the group's resources");
            await Task.Delay(200);
        }
    }
}
