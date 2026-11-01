using Rebalancer.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>Requires Docker. ADR-0023: the health check with a real Redis client.</summary>
public sealed class RedisHealthCheckTests
{
    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public async Task TheCheckFollowsARealClient(string image)
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(image).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-health-" + Guid.NewGuid().ToString("N");
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, HealthCheckScenarios.Resources);

        await HealthCheckScenarios.TheCheckFollowsARealClient(group, () => new RedisProvider(connectionString));
    }
}
