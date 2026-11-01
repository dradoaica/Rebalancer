using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.PostgreSql;
using Rebalancer.PostgreSql.Leases;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. US-0026 criterion 8, TM-0004 D2: a node that stalls while its lease transaction holds the
///     group row does not block the others beyond the lease expiry. The server ends the stalled transaction
///     (<c>idle_in_transaction_session_timeout</c>) and the waiting nodes give up and retry (<c>lock_timeout</c>), both
///     set by the backend to half the lease expiry.
/// </summary>
public sealed class PostgreSqlLockStallTests
{
    private const int LeaseExpirySeconds = 5;

    [SkippableFact]
    public async Task ANodeStalledHoldingTheGroupRow_DoesNotStopTheOthers()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-stall-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, ["r1",], LeaseExpirySeconds, 1);

        // the stalled node: takes the group row, then stalls far longer than the lease expiry
        var holdsRow = new TaskCompletionSource();
        var stalled = new LeaseService(connectionString, new NullRebalancerLogger())
        {
            AfterGroupRowLocked = async () =>
            {
                holdsRow.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(60));
            },
        };
        var stalledAcquire = stalled.TryAcquireLeaseAsync(
            new AcquireLeaseRequest
            {
                ClientId = Guid.NewGuid(),
                ResourceGroup = group,
            }
        );
        await holdsRow.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // two healthy nodes start while the row is held; one of them must be assigned r1 well before the stall ends
        var assigned = new TaskCompletionSource();
        using var first = new RebalancerClient(new PostgreSqlProvider(connectionString));
        using var second = new RebalancerClient(new PostgreSqlProvider(connectionString));
        foreach (var client in new[] { first, second, })
        {
            client.OnAssignment += (_, args) =>
            {
                if (args.Resources.Contains("r1"))
                {
                    assigned.TrySetResult();
                }
            };
        }

        var options = new ClientOptions
        {
            AutoRecoveryOnError = true,
            RestartDelay = TimeSpan.FromSeconds(1),
        };
        await first.StartAsync(group, options);
        await second.StartAsync(group, options);

        var continued = await Task.WhenAny(assigned.Task, Task.Delay(TimeSpan.FromSeconds(20))) == assigned.Task;
        Assert.True(continued, "the healthy nodes were blocked by the stalled node's row lock");

        // the stalled node's transaction was ended by the server: it was not granted the lease
        var stalledResult = await stalledAcquire.WaitAsync(TimeSpan.FromSeconds(90));
        Assert.NotEqual(LeaseResult.Granted, stalledResult.Result);

        await first.StopAsync(TimeSpan.FromSeconds(10));
        await second.StopAsync(TimeSpan.FromSeconds(10));
    }
}
