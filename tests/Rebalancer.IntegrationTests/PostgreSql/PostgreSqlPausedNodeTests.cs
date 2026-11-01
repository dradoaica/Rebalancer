using Rebalancer.Core;
using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. A node that stops responding past the lease / keep-alive expiry and then resumes (PRB-0003,
///     ADR-0019; PRB-0001; ADR-0026), or whose store call fails once (PRB-0005), against a real PostgreSQL.
/// </summary>
public sealed class PostgreSqlPausedNodeTests
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
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-pause-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources, 5, 1);

        await scenario(
            group,
            () => new PostgreSqlProvider(connectionString),
            pause => PausablePostgreSqlServices.CreateProvider(connectionString, pause)
        );
    }
}
