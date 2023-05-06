namespace Rebalancer.SqlServer.Leases;

using System;

public class Lease
{
    public string ResourceGroup { get; set; }
    public Guid ClientId { get; set; }
    public int FencingToken { get; set; }
    public TimeSpan ExpiryPeriod { get; set; }
    public TimeSpan HeartbeatPeriod { get; set; }
}
