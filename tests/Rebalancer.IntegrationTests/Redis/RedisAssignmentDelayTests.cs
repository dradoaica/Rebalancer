using Rebalancer.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>Requires Docker. PRB-0007: an assignment delay longer than the keep-alive expiry, on Redis.</summary>
public sealed class RedisAssignmentDelayTests
{
    [SkippableFact]
    public async Task ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-delay-" + Guid.NewGuid().ToString("N");
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, AssignmentDelayScenarios.Resources);

        await AssignmentDelayScenarios.ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable(
            group,
            () => new RedisProvider(connectionString)
        );
    }
}
