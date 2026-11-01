using System;

namespace Rebalancer.LeaseCoordination.Leases;

public class AcquireLeaseRequest
{
    public Guid ClientId { get; set; }
    public string ResourceGroup { get; set; }
}
