using System;

namespace Rebalancer.Redis.Utils;

[Obsolete("Not part of the supported API; internal from Rebalancer 6.0 (ADR-0034).", DiagnosticId = "RBR0034")]
public static class Constants
{
    public const string SCHEMA = "RBR";
}
