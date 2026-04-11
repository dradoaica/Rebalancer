using Rebalancer.Core;
using Rebalancer.Redis;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using StackExchange.Redis.DataTypes.Collections;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>Requires Docker. Validates that two nodes never both report the same resource as assigned (sampled).</summary>
public sealed class RedisAssignmentSafetyTests
{
    [SkippableFact]
    public async Task TwoClients_OneResource_AreNeverBothAssignedTheSameResource()
    {
        Skip.IfNot(
            DockerSupport.IsDockerAvailable(),
            "Docker is not available; start Docker to run integration tests."
        );

        await using var redis = new RedisBuilder().Build();
        await redis.StartAsync();

        var connectionString = redis.GetConnectionString();
        var group = "it-redis-" + Guid.NewGuid().ToString("N");
        const string resourceName = "only-resource";

        await SeedRedisResourceAsync(connectionString, group, resourceName);

        var options = new ClientOptions
        {
            AutoRecoveryOnError = true,
            RestartDelay = TimeSpan.FromSeconds(1),
        };

        var redisBackend1 = new RedisProvider(connectionString);
        var redisBackend2 = new RedisProvider(connectionString);
        using var client1 = new RebalancerClient(redisBackend1);
        using var client2 = new RebalancerClient(redisBackend2);

        await client1.StartAsync(group, options);
        await client2.StartAsync(group, options);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                var a = client1.GetAssignedResources().Resources ?? [];
                var b = client2.GetAssignedResources().Resources ?? [];
                Assert.False(
                    a.Contains(resourceName) && b.Contains(resourceName),
                    "Invariant violated: both clients hold the same resource."
                );
                await Task.Delay(200);
            }
        }
        finally
        {
            await client1.StopAsync(TimeSpan.FromSeconds(15));
            await client2.StopAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task SeedRedisResourceAsync(string connectionString, string group, string resourceName)
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        try
        {
            var db = mux.GetDatabase();
            var list = new RedisList<Resource>(db, $"{Constants.SCHEMA}:Resources");
            list.Add(
                new Resource
                {
                    ResourceGroup = group,
                    ResourceName = resourceName,
                }
            );
        }
        finally
        {
            mux.Dispose();
        }
    }
}
