using Microsoft.Data.SqlClient;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.SqlServer.Leases;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.SqlServer;

/// <summary>
///     Requires Docker. A14 of the CR-0013 cross-check, as ADR-0030 Clock A for PostgreSQL: a lease request that
///     waited for the group row judges the lease's age by the database time after the wait, not the time its statement
///     started.
/// </summary>
public sealed class SqlServerLeaseClockTests
{
    [SkippableFact]
    public async Task AnAcquireThatWaitedForTheRowLock_SeesTheLeaseExpireDuringTheWait()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var sql = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
        await sql.StartAsync();
        var cs = sql.GetConnectionString();
        await SchemaSetup.ApplyRbrSchemaAsync(cs);
        const string group = "orders";
        await SchemaSetup.SeedResourceGroupAsync(cs, group, ["r1",], 4, 1);

        var first = new LeaseService(cs, new NullRebalancerLogger());
        Assert.Equal(
            LeaseResult.Granted,
            (await first.TryAcquireLeaseAsync(
                new AcquireLeaseRequest
                {
                    ClientId = Guid.NewGuid(),
                    ResourceGroup = group,
                }
            )).Result
        );

        // another session holds the group row for 8 s, past the 4 s lease expiry
        await using var blocker = new SqlConnection(cs);
        await blocker.OpenAsync();
        await using var blockingTransaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var lockRow = blocker.CreateCommand())
        {
            lockRow.Transaction = blockingTransaction;
            lockRow.CommandText = "UPDATE RBR.ResourceGroups SET LockedByClient = NEWID() WHERE ResourceGroup = @g";
            lockRow.Parameters.AddWithValue("@g", group);
            await lockRow.ExecuteNonQueryAsync();
        }

        // starts while the lease is a fraction of a second old and gets the row after it expired
        var second = new LeaseService(cs, new NullRebalancerLogger()).TryAcquireLeaseAsync(
            new AcquireLeaseRequest
            {
                ClientId = Guid.NewGuid(),
                ResourceGroup = group,
            }
        );
        await Task.Delay(TimeSpan.FromSeconds(8));
        Assert.False(second.IsCompleted);
        await blockingTransaction.CommitAsync();

        var response = await second.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(LeaseResult.Granted, response.Result);
    }
}
