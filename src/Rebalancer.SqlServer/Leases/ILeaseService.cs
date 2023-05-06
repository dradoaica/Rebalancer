namespace Rebalancer.SqlServer.Leases;

using System.Threading.Tasks;

public interface ILeaseService
{
    Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest acquireLeaseRequest);
    Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest renewLeaseRequest);
    Task RelinquishLeaseAsync(RelinquishLeaseRequest relinquishLeaseRequest);
}
