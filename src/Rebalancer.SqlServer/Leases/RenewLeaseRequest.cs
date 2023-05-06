namespace Rebalancer.SqlServer.Leases;

using System;

public class RenewLeaseRequest
{
    public Guid ClientId { get; set; }
    public string ResourceGroup { get; set; }
    public int FencingToken { get; set; }
}
