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
    [SkippableFact]
    public Task FollowerPausedPastExpiry_StopsAtFirstTickAfterResume() =>
        RunAsync(PausedNodeScenarios.FollowerPausedPastExpiry_StopsAtFirstTickAfterResume);

    [SkippableFact]
    public Task CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume() =>
        RunAsync(PausedNodeScenarios.CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume);

    [SkippableFact]
    public Task CoordinatorStalledAtTheBarrier_StartsNothingAfterResume() =>
        RunAsync(PausedNodeScenarios.CoordinatorStalledAtTheBarrier_StartsNothingAfterResume);

    [SkippableFact]
    public Task CoordinatorStalledAfterGranting_StopsAtFirstTickAfterResume() =>
        RunAsync(PausedNodeScenarios.CoordinatorStalledAfterGranting_StopsAtFirstTickAfterResume);

    [SkippableFact]
    public Task FollowerRecoversFromAStoreError_IsAssignedAgain() =>
        RunAsync(PausedNodeScenarios.FollowerRecoversFromAStoreError_IsAssignedAgain);

    [SkippableFact]
    public Task ACleanStop_RaisesOnUnassignmentBeforeItReturns() =>
        RunAsync(PausedNodeScenarios.ACleanStop_RaisesOnUnassignmentBeforeItReturns);

    [SkippableFact]
    public Task AStoppedClient_ReportsStopped() => RunAsync(PausedNodeScenarios.AStoppedClient_ReportsStopped);

    private static async Task RunAsync(
        Func<string, Func<IRebalancerProvider>, Func<NodePause, IRebalancerProvider>, Task> scenario
    )
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
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
