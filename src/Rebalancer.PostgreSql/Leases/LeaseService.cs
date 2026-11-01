using Npgsql;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.PostgreSql.Connections;
using System;
using System.Data;
using System.Threading.Tasks;

namespace Rebalancer.PostgreSql.Leases;

/// <summary>
///     The lease of a resource group (ADR-0030): one READ COMMITTED transaction locks the group row with
///     <c>SELECT … FOR UPDATE</c>, then reads the time with <c>clock_timestamp()</c>, so a node that waited for the lock
///     never stamps a lease with a time from before its wait (<c>now()</c> is fixed at the transaction start).
/// </summary>
internal class LeaseService : ILeaseService
{
    // TM-0004 D2: the stall bound (StallBound) comes first, then the row lock, then the time read after the lock
    private const string LockGroupRow = StallBound.SetTimeouts + @"
SELECT coordinator_id, last_coordinator_renewal, fencing_token, lease_expiry_seconds, heartbeat_seconds
  FROM rbr.resource_groups
 WHERE resource_group = @ResourceGroup
   FOR UPDATE;
SELECT clock_timestamp();";

    private readonly string connectionString;
    private readonly IRebalancerLogger logger;

    public LeaseService(string connectionString, IRebalancerLogger logger)
    {
        this.connectionString = connectionString;
        this.logger = logger;
    }

    // test seam (US-0026, criterion 8): runs while this node holds the group row, before it decides
    internal Func<Task> AfterGroupRowLocked { get; set; }

    public async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest acquireLeaseRequest)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        // disposing an uncommitted transaction rolls it back, also on the early returns below
        await using var transaction = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var rg = await LockGroupRowAsync(conn, transaction, acquireLeaseRequest.ResourceGroup);
            if (rg == null)
            {
                return new LeaseResponse
                {
                    Result = LeaseResult.NoLease,
                    Lease = new Lease
                    {
                        ExpiryPeriod = TimeSpan.FromMinutes(1),
                        HeartbeatPeriod = TimeSpan.FromSeconds(25),
                    },
                };
            }

            // if the coordinator is empty or its lease expired then grant, else deny
            LeaseResponse response = new()
            {
                Lease = new Lease(),
            };
            if (rg.CoordinatorId == Guid.Empty ||
                (rg.TimeNow - rg.LastCoordinatorRenewal).TotalSeconds > rg.LeaseExpirySeconds)
            {
                response.Lease.ResourceGroup = acquireLeaseRequest.ResourceGroup;
                response.Lease.ClientId = acquireLeaseRequest.ClientId;
                response.Lease.ExpiryPeriod = TimeSpan.FromSeconds(rg.LeaseExpirySeconds);
                response.Lease.HeartbeatPeriod = TimeSpan.FromSeconds(rg.HeartbeatSeconds);
                response.Lease.FencingToken = ++rg.FencingToken;
                response.Result = LeaseResult.Granted;

                await using var command = new NpgsqlCommand(
                    @"UPDATE rbr.resource_groups
   SET coordinator_id = @ClientId,
       last_coordinator_renewal = clock_timestamp(),
       coordinator_server = @Server,
       locked_by_client = @ClientId,
       fencing_token = @FencingToken
 WHERE resource_group = @ResourceGroup",
                    conn,
                    transaction
                );
                command.Parameters.AddWithValue("ClientId", acquireLeaseRequest.ClientId);
                command.Parameters.AddWithValue("Server", Environment.MachineName);
                command.Parameters.AddWithValue("FencingToken", response.Lease.FencingToken);
                command.Parameters.AddWithValue("ResourceGroup", acquireLeaseRequest.ResourceGroup);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                response.Lease.ExpiryPeriod = TimeSpan.FromSeconds(rg.LeaseExpirySeconds);
                response.Lease.HeartbeatPeriod = TimeSpan.FromSeconds(rg.HeartbeatSeconds);
                response.Result = LeaseResult.Denied;
            }

            await transaction.CommitAsync();

            return response;
        }
        catch (Exception ex)
        {
            await RollBackAsync(transaction, acquireLeaseRequest.ClientId, "lease acquisition", ex);
            return new LeaseResponse
            {
                Result = TransientErrorDetector.IsTransient(ex) ? LeaseResult.TransientError : LeaseResult.Error,
                Message = "Lease acquisition failure",
                Exception = ex,
            };
        }
    }

    public async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest renewLeaseRequest)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        // disposing an uncommitted transaction rolls it back, also on the early returns below
        await using var transaction = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var rg = await LockGroupRowAsync(conn, transaction, renewLeaseRequest.ResourceGroup);
            if (rg == null)
            {
                return new LeaseResponse
                {
                    Result = LeaseResult.NoLease,
                };
            }

            // grant only if this client is the coordinator with the same fencing token, else deny
            if (!rg.CoordinatorId.Equals(renewLeaseRequest.ClientId) ||
                rg.FencingToken > renewLeaseRequest.FencingToken)
            {
                return new LeaseResponse
                {
                    Result = LeaseResult.Denied,
                };
            }

            LeaseResponse response = new()
            {
                Lease = new Lease(),
            };
            if ((rg.TimeNow - rg.LastCoordinatorRenewal).TotalSeconds <= rg.LeaseExpirySeconds)
            {
                response.Lease.ResourceGroup = renewLeaseRequest.ResourceGroup;
                response.Lease.ClientId = renewLeaseRequest.ClientId;
                response.Lease.ExpiryPeriod = TimeSpan.FromSeconds(rg.LeaseExpirySeconds);
                response.Lease.HeartbeatPeriod = TimeSpan.FromSeconds(rg.HeartbeatSeconds);
                response.Lease.FencingToken = rg.FencingToken;
                response.Result = LeaseResult.Granted;

                await using var command = new NpgsqlCommand(
                    @"UPDATE rbr.resource_groups
   SET last_coordinator_renewal = clock_timestamp(),
       locked_by_client = @ClientId
 WHERE resource_group = @ResourceGroup",
                    conn,
                    transaction
                );
                command.Parameters.AddWithValue("ClientId", renewLeaseRequest.ClientId);
                command.Parameters.AddWithValue("ResourceGroup", renewLeaseRequest.ResourceGroup);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                response.Result = LeaseResult.Denied;
            }

            await transaction.CommitAsync();

            return response;
        }
        catch (Exception ex)
        {
            await RollBackAsync(transaction, renewLeaseRequest.ClientId, "lease renewal", ex);
            return new LeaseResponse
            {
                Result = TransientErrorDetector.IsTransient(ex) ? LeaseResult.TransientError : LeaseResult.Error,
                Message = "Lease renew failure",
                Exception = ex,
            };
        }
    }

    public async Task RelinquishLeaseAsync(RelinquishLeaseRequest relinquishLeaseRequest)
    {
        await using var conn = await ConnectionHelper.GetOpenConnectionAsync(connectionString);
        await using var command = conn.CreateCommand();
        command.CommandText = @"UPDATE rbr.resource_groups
   SET coordinator_id = NULL
 WHERE resource_group = @ResourceGroup
   AND coordinator_id = @ClientId
   AND fencing_token = @FencingToken";
        command.Parameters.AddWithValue("ResourceGroup", relinquishLeaseRequest.ResourceGroup);
        command.Parameters.AddWithValue("ClientId", relinquishLeaseRequest.ClientId);
        command.Parameters.AddWithValue("FencingToken", relinquishLeaseRequest.FencingToken);
        await command.ExecuteNonQueryAsync();
    }

    // null when the group does not exist; the row stays locked until the transaction ends
    private async Task<ResourceGroup> LockGroupRowAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction transaction,
        string resourceGroup
    )
    {
        ResourceGroup rg = null;
        await using (var command = new NpgsqlCommand(LockGroupRow, conn, transaction))
        {
            command.Parameters.AddWithValue("ResourceGroup", resourceGroup);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.NextResultAsync(); // past the timeouts, to the locked row
            if (await reader.ReadAsync())
            {
                rg = new ResourceGroup
                {
                    Name = resourceGroup,
                    CoordinatorId = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                    LastCoordinatorRenewal =
                        reader.IsDBNull(1) ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            : reader.GetDateTime(1),
                    FencingToken = reader.GetInt32(2),
                    LeaseExpirySeconds = reader.GetInt32(3),
                    HeartbeatSeconds = reader.GetInt32(4),
                };
            }

            await reader.NextResultAsync(); // the time, read after the lock
            await reader.ReadAsync();
            if (rg != null)
            {
                rg.TimeNow = reader.GetDateTime(0);
            }
        }

        if (rg != null && AfterGroupRowLocked != null)
        {
            await AfterGroupRowLocked();
        }

        return rg;
    }

    private async Task RollBackAsync(NpgsqlTransaction transaction, Guid clientId, string operation, Exception ex)
    {
        try
        {
            logger.Error(clientId.ToString(), $"Rolling back {operation}", ex);
            await transaction.RollbackAsync();
        }
        catch (Exception rex)
        {
            logger.Error(clientId.ToString(), $"Rollback of {operation} failed", rex);
        }
    }
}
