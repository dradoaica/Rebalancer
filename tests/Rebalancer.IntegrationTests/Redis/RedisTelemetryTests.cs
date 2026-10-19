using Rebalancer.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>Requires Docker. ADR-0022 telemetry of the Redis backend.</summary>
public sealed class RedisTelemetryTests
{
    [SkippableFact]
    public async Task TwoNodes_EmitElectionRebalanceAndAssignments()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-otel-" + Guid.NewGuid().ToString("N");
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources);

        await TelemetryScenarios.TwoNodes_EmitElectionRebalanceAndAssignments(
            group, "redis", PausedNodeScenarios.Resources, () => new RedisProvider(connectionString)
        );
    }
}
