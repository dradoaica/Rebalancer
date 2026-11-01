using Rebalancer.Core;
using Rebalancer.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. A node that stops responding past the lease / keep-alive expiry and then resumes (PRB-0003,
///     ADR-0019; PRB-0001; ADR-0026), or whose store call fails once (PRB-0005), against a real Redis.
/// </summary>
public sealed class RedisPausedNodeTests
{
    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task FollowerPausedPastExpiry_StopsAtFirstTickAfterResume(string image) =>
        RunAsync(image, PausedNodeScenarios.FollowerPausedPastExpiry_StopsAtFirstTickAfterResume);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume(string image) =>
        RunAsync(image, PausedNodeScenarios.CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task CoordinatorStalledAtTheBarrier_StartsNothingAfterResume(string image) =>
        RunAsync(image, PausedNodeScenarios.CoordinatorStalledAtTheBarrier_StartsNothingAfterResume);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task CoordinatorStalledAfterGranting_StopsAtFirstTickAfterResume(string image) =>
        RunAsync(image, PausedNodeScenarios.CoordinatorStalledAfterGranting_StopsAtFirstTickAfterResume);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task FollowerRecoversFromAStoreError_IsAssignedAgain(string image) =>
        RunAsync(image, PausedNodeScenarios.FollowerRecoversFromAStoreError_IsAssignedAgain);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task ACleanStop_RaisesOnUnassignmentBeforeItReturns(string image) =>
        RunAsync(image, PausedNodeScenarios.ACleanStop_RaisesOnUnassignmentBeforeItReturns);

    [SkippableTheory]
    [MemberData(nameof(DockerSupport.RedisServers), MemberType = typeof(DockerSupport))]
    public Task AStoppedClient_ReportsStopped(string image) =>
        RunAsync(image, PausedNodeScenarios.AStoppedClient_ReportsStopped);

    private static async Task RunAsync(
        string image,
        Func<string, Func<IRebalancerProvider>, Func<NodePause, IRebalancerProvider>, Task> scenario
    )
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(image).Build();
        await redis.StartAsync();
        var connectionString = redis.GetConnectionString();
        var group = "it-redis-pause-" + Guid.NewGuid().ToString("N");
        await RedisSeed.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources);

        // the paused node's services share this connection; the provider opens its own as well
        using var pausedNodeConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await scenario(
            group,
            () => new RedisProvider(connectionString),
            pause => PausableRedisServices.CreateProvider(connectionString, pausedNodeConnection.GetDatabase(), pause)
        );
    }
}
