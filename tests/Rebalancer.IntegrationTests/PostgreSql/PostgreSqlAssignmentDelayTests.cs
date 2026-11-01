using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>Requires Docker. PRB-0007: an assignment delay longer than the keep-alive expiry, on PostgreSQL.</summary>
public sealed class PostgreSqlAssignmentDelayTests
{
    [SkippableFact]
    public async Task ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-delay-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(
            connectionString,
            group,
            AssignmentDelayScenarios.Resources,
            5,
            1
        );

        await AssignmentDelayScenarios.ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable(
            group,
            () => new PostgreSqlProvider(connectionString)
        );
    }
}
