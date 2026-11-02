using System;

namespace Rebalancer.Redis.Resources;

[Obsolete("Not part of the supported API; internal from Rebalancer 6.0 (ADR-0034).", DiagnosticId = "RBR0034")]
public class Resource
{
    public string ResourceGroup { get; set; }
    public string ResourceName { get; set; }
}
