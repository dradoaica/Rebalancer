using Rebalancer.Core;
using Rebalancer.SqlServer;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>
///     Requires Docker. A node that stops responding past the lease / keep-alive expiry and then resumes (PRB-0003,
///     ADR-0019; PRB-0001; ADR-0026), or whose store call fails once (PRB-0005), against a real SQL Server.
/// </summary>
public sealed class SqlServerPausedNodeTests
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
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var connectionString = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(connectionString);
        var group = "it-sql-pause-" + Guid.NewGuid().ToString("N");
        await SchemaSetup.SeedResourceGroupAsync(connectionString, group, PausedNodeScenarios.Resources, 5, 1);

        await scenario(
            group,
            () => new SqlServerProvider(connectionString),
            pause => PausableSqlServerServices.CreateProvider(connectionString, pause)
        );
    }
}
