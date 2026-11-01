using System;

namespace Rebalancer.LeaseCoordination.Leases;

public class RenewLeaseRequest
{
    public Guid ClientId { get; set; }
    public string ResourceGroup { get; set; }
    public int FencingToken { get; set; }
}
