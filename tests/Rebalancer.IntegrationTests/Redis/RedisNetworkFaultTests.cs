using Rebalancer.Core;
using Rebalancer.Redis;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. Nodes whose link to Redis is slow and lossy come and go; no resource is ever processed by two
///     nodes at once, and the group converges.
/// </summary>
public sealed class RedisNetworkFaultTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public async Task NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(string image)
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(image).Build();
        await redis.StartAsync();
        var group = "it-redis-network-" + Guid.NewGuid().ToString("N");
        string[] resources = ["r1", "r2", "r3", "r4", "r5", "r6",];
        await RedisSeed.SeedResourceGroupAsync(redis.GetConnectionString(), group, resources);

        await using var proxy = new TcpFaultProxy(redis.Hostname, redis.GetMappedPublicPort(6379));
        proxy.Degrade(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(50),
            0.02,
            TimeSpan.FromMilliseconds(500)
        );

        await NetworkFaultScenarios.NodesComingAndGoingOverASlowLossyNetwork_NeverProcessAResourceTwiceAtOnce(
            group,
            resources,
            () => new RedisProvider(proxy.Endpoint),
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromSeconds(1),
            },
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90),
            output.WriteLine
        );
    }
}
