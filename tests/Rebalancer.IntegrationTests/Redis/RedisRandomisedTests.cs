using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>Requires Docker. US-0019 randomised assignment safety and liveness on Redis.</summary>
public sealed class RedisRandomisedTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task RandomStartsStopsPausesAndKills_KeepAssignmentsSafeAndLive()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-random-" + Guid.NewGuid().ToString("N");
        var resources = RandomisedScenario.ResourceNames(10);
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, resources);

        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await new RandomisedScenario(
            group,
            resources,
            // ReSharper disable once AccessToDisposedClosure
            pause => PausableRedisServices.CreateProvider(connectionString, connection.GetDatabase(), pause),
            output.WriteLine
        ).RunAsync();
    }
}
