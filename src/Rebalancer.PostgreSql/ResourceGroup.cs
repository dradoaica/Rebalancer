using System;

namespace Rebalancer.PostgreSql;

internal class ResourceGroup
{
    public string Name { get; set; }
    public Guid CoordinatorId { get; set; }
    public DateTime LastCoordinatorRenewal { get; set; }
    public DateTime TimeNow { get; set; }
    public int FencingToken { get; set; }
    public int LeaseExpirySeconds { get; set; }
    public int HeartbeatSeconds { get; set; }
}
