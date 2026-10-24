using Rebalancer.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>Requires Docker. ADR-0023: the health check with a real Redis client.</summary>
public sealed class RedisHealthCheckTests
{
    [SkippableFact]
    public async Task TheCheckFollowsARealClient()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-health-" + Guid.NewGuid().ToString("N");
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, HealthCheckScenarios.Resources);

        await HealthCheckScenarios.TheCheckFollowsARealClient(group, () => new RedisProvider(connectionString));
    }
}
