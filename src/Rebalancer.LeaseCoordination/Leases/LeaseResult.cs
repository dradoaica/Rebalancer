namespace Rebalancer.LeaseCoordination.Leases;

public enum LeaseResult
{
    NoLease,
    Granted,
    Denied,
    TransientError,
    Error,
}
