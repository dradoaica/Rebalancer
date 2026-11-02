using System;

namespace Rebalancer.LeaseCoordination.Roles;

[Obsolete("Not part of the supported API; internal from Rebalancer 6.0 (ADR-0034).", DiagnosticId = "RBR0034")]
public class ClientEvent
{
    public EventType EventType { get; set; }
    public int FencingToken { get; set; }
    public string ResourceGroup { get; set; }
    public TimeSpan KeepAliveExpiryPeriod { get; set; }
    public TimeSpan ClientRecordRetention { get; set; }
    public TimeSpan OnAssignmentDelay { get; set; }
    public CoordinatorToken CoordinatorToken { get; set; }
}
