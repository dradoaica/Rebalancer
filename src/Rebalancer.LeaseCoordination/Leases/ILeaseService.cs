using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination.Leases;

/// <summary>
///     The coordinator lease of a resource group (ADR-0029). An implementation decides each request atomically
///     against the stored lease and judges expiry by the store's clock, never the node's (ADR-0017). Errors are returned
///     as <see cref="LeaseResult.TransientError" /> (retried) or <see cref="LeaseResult.Error" />, not thrown.
/// </summary>
public interface ILeaseService
{
    /// <summary>
    ///     Grants the lease when no coordinator holds it or its lease has expired, with a fencing token one higher than
    ///     the last; otherwise <see cref="LeaseResult.Denied" />; <see cref="LeaseResult.NoLease" /> when the group does not
    ///     exist.
    /// </summary>
    Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest acquireLeaseRequest);

    /// <summary>
    ///     Renews only while the lease still names this client with this fencing token and has not expired; otherwise
    ///     <see cref="LeaseResult.Denied" />.
    /// </summary>
    Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest renewLeaseRequest);

    /// <summary>Releases the lease only if it still names this client with this fencing token.</summary>
    Task RelinquishLeaseAsync(RelinquishLeaseRequest relinquishLeaseRequest);
}
