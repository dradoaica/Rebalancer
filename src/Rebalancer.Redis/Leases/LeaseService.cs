using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.Redis.Connections;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Leases;

internal class LeaseService : ILeaseService
{
    private readonly IDatabase cache;

    public LeaseService(IDatabase cache)
    {
        this.cache = cache;
    }

    public async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest acquireLeaseRequest)
    {
        try
        {
            return await RedisHash.CompareAndSetAsync<ResourceGroup, LeaseResponse>(
                cache,
                RedisHash.LeaseKey(acquireLeaseRequest.ResourceGroup),
                RedisHash.Encode(acquireLeaseRequest.ResourceGroup),
                (resourceGroup, now) => DecideAcquire(resourceGroup, acquireLeaseRequest, now)
            );
        }
        catch (Exception ex)
        {
            // never write on failure: the stored lease may belong to another, healthy coordinator
            return ErrorResponse(ex);
        }
    }

    public async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest renewLeaseRequest)
    {
        try
        {
            return await RedisHash.CompareAndSetAsync<ResourceGroup, LeaseResponse>(
                cache,
                RedisHash.LeaseKey(renewLeaseRequest.ResourceGroup),
                RedisHash.Encode(renewLeaseRequest.ResourceGroup),
                (resourceGroup, now) => DecideRenew(resourceGroup, renewLeaseRequest, now)
            );
        }
        catch (Exception ex)
        {
            return ErrorResponse(ex);
        }
    }

    public Task RelinquishLeaseAsync(RelinquishLeaseRequest relinquishLeaseRequest) =>
        RedisHash.CompareAndSetAsync<ResourceGroup, bool>(
            cache,
            RedisHash.LeaseKey(relinquishLeaseRequest.ResourceGroup),
            RedisHash.Encode(relinquishLeaseRequest.ResourceGroup),
            (resourceGroup, _) => DecideRelinquish(resourceGroup, relinquishLeaseRequest)
        );

    internal static (ResourceGroup NewValue, LeaseResponse Result) DecideAcquire(
        ResourceGroup resourceGroup,
        AcquireLeaseRequest request,
        DateTime now
    )
    {
        if (resourceGroup == null)
        {
            return (null, new LeaseResponse
            {
                Result = LeaseResult.NoLease,
                Lease = new Lease
                {
                    ExpiryPeriod = TimeSpan.FromMinutes(1),
                    HeartbeatPeriod = TimeSpan.FromSeconds(25),
                },
            });
        }

        var lease = new Lease
        {
            ExpiryPeriod = TimeSpan.FromSeconds(resourceGroup.LeaseExpirySeconds),
            HeartbeatPeriod = TimeSpan.FromSeconds(resourceGroup.HeartbeatSeconds),
        };

        // grant only if there is no coordinator or its lease has expired
        if (resourceGroup.CoordinatorId != Guid.Empty && !IsExpired(resourceGroup, now))
        {
            return (null, new LeaseResponse
            {
                Result = LeaseResult.Denied,
                Lease = lease,
            });
        }

        resourceGroup.FencingToken++;
        resourceGroup.CoordinatorId = request.ClientId;
        resourceGroup.LockedByClientId = request.ClientId;
        resourceGroup.LastCoordinatorRenewal = now;
        resourceGroup.TimeNow = now;
        resourceGroup.CoordinatorServer = Environment.MachineName;

        lease.ResourceGroup = request.ResourceGroup;
        lease.ClientId = request.ClientId;
        lease.FencingToken = resourceGroup.FencingToken;
        return (resourceGroup, new LeaseResponse
        {
            Result = LeaseResult.Granted,
            Lease = lease,
        });
    }

    internal static (ResourceGroup NewValue, LeaseResponse Result) DecideRenew(
        ResourceGroup resourceGroup,
        RenewLeaseRequest request,
        DateTime now
    )
    {
        if (resourceGroup == null)
        {
            return (null, new LeaseResponse
            {
                Result = LeaseResult.NoLease,
            });
        }

        if (resourceGroup.CoordinatorId != request.ClientId ||
            resourceGroup.FencingToken > request.FencingToken ||
            IsExpired(resourceGroup, now))
        {
            return (null, new LeaseResponse
            {
                Result = LeaseResult.Denied,
            });
        }

        resourceGroup.LastCoordinatorRenewal = now;
        resourceGroup.TimeNow = now;
        return (resourceGroup, new LeaseResponse
        {
            Result = LeaseResult.Granted,
            Lease = new Lease
            {
                ResourceGroup = request.ResourceGroup,
                ClientId = request.ClientId,
                FencingToken = resourceGroup.FencingToken,
                ExpiryPeriod = TimeSpan.FromSeconds(resourceGroup.LeaseExpirySeconds),
                HeartbeatPeriod = TimeSpan.FromSeconds(resourceGroup.HeartbeatSeconds),
            },
        });
    }

    internal static (ResourceGroup NewValue, bool Result) DecideRelinquish(
        ResourceGroup resourceGroup,
        RelinquishLeaseRequest request
    )
    {
        // only the current holder of this exact lease may give it up
        if (resourceGroup == null ||
            resourceGroup.CoordinatorId != request.ClientId ||
            resourceGroup.FencingToken != request.FencingToken)
        {
            return (null, false);
        }

        resourceGroup.CoordinatorId = Guid.Empty;
        return (resourceGroup, true);
    }

    private static bool IsExpired(ResourceGroup resourceGroup, DateTime now) =>
        (now - resourceGroup.LastCoordinatorRenewal).TotalSeconds > resourceGroup.LeaseExpirySeconds;

    private static LeaseResponse ErrorResponse(Exception ex) =>
        new()
        {
            Result = TransientErrorDetector.IsTransient(ex) ? LeaseResult.TransientError : LeaseResult.Error,
            Message = "Lease acquisition failure",
            Exception = ex,
        };
}
