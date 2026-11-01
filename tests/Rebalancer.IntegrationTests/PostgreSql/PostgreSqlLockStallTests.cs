using Npgsql;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.PostgreSql;
using Rebalancer.PostgreSql.Leases;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. US-0026 criterion 8, TM-0004 D2: a node that stalls while its lease transaction holds the
///     group row does not block the others beyond the lease expiry. The server ends the stalled transaction (
///     <c>idle_in_transaction_session_timeout</c>) and the waiting nodes give up and retry (<c>lock_timeout</c>), both set
///     by the backend to half the lease expiry.
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
        foreach (var client in new[]
                 {
                     first, second,
                 })
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
        try
        {
            await first.StartAsync(group, options);
            await second.StartAsync(group, options);

            // 20 s: half the lease expiry until the server ends the stall, the waiting nodes' retries (2 s, 4 s), and
            // the assignment itself (about 6 s); without the bound the nodes wait for the whole 60 s stall
            var continued = await Task.WhenAny(assigned.Task, Task.Delay(TimeSpan.FromSeconds(20))) == assigned.Task;
            Assert.True(continued, "the healthy nodes were blocked by the stalled node's row lock");

            // the server ended the stalled node's session: its lease call failed instead of completing
            var stalledResult = await stalledAcquire.WaitAsync(TimeSpan.FromSeconds(90));
            Assert.NotEqual(LeaseResult.Granted, stalledResult.Result);
            Assert.NotNull(stalledResult.Exception);
        }
        finally
        {
            await first.StopAsync(TimeSpan.FromSeconds(10));
            await second.StopAsync(TimeSpan.FromSeconds(10));
        }
    }

    // US-0026 criterion 4, ADR-0030: a renewal that waited for the row lock is stamped with the time after the wait
    // (clock_timestamp() read after the lock), not the time its transaction started (now())
    [SkippableFact]
    public async Task ARenewalThatWaitedForTheRowLock_IsStampedAfterTheWait()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        var group = "it-pg-clock-" + Guid.NewGuid().ToString("N");
        await PostgreSqlSchema.SeedResourceGroupAsync(connectionString, group, ["r1",], 60, 1);

        var coordinatorId = Guid.NewGuid();
        var coordinator = new LeaseService(connectionString, new NullRebalancerLogger());
        var lease = await coordinator.TryAcquireLeaseAsync(
            new AcquireLeaseRequest
            {
                ClientId = coordinatorId,
                ResourceGroup = group,
            }
        );
        Assert.Equal(LeaseResult.Granted, lease.Result);

        // another node holds the group row for 3 s (it is denied: the lease is still valid)
        var holdsRow = new TaskCompletionSource();
        var other = new LeaseService(connectionString, new NullRebalancerLogger())
        {
            AfterGroupRowLocked = async () =>
            {
                holdsRow.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(3));
            },
        };
        var otherAcquire = other.TryAcquireLeaseAsync(
            new AcquireLeaseRequest
            {
                ClientId = Guid.NewGuid(),
                ResourceGroup = group,
            }
        );
        await holdsRow.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var beforeRenewal = await DatabaseTimeAsync(connectionString);
        var renewal = await coordinator.TryRenewLeaseAsync(
            new RenewLeaseRequest
            {
                ClientId = coordinatorId,
                ResourceGroup = group,
                FencingToken = lease.Lease.FencingToken,
            }
        );
        Assert.Equal(LeaseResult.Granted, renewal.Result);
        Assert.Equal(LeaseResult.Denied, (await otherAcquire).Result);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_coordinator_renewal FROM rbr.resource_groups WHERE resource_group = @g";
        cmd.Parameters.AddWithValue("g", group);
        var stamped = (DateTime)(await cmd.ExecuteScalarAsync())!;
        // the renewal waited about 3 s for the row; now() would stamp it within a few ms of beforeRenewal
        Assert.True(
            stamped - beforeRenewal >= TimeSpan.FromSeconds(2),
            $"renewal stamped {(stamped - beforeRenewal).TotalMilliseconds} ms after its start"
        );
    }

    private static async Task<DateTime> DatabaseTimeAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT clock_timestamp()";
        return (DateTime)(await cmd.ExecuteScalarAsync())!;
    }
}
