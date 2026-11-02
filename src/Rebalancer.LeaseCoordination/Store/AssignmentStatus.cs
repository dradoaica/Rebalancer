using System;

namespace Rebalancer.LeaseCoordination.Store;

[Obsolete("Not part of the supported API; internal from Rebalancer 6.0 (ADR-0034).", DiagnosticId = "RBR0034")]
public enum AssignmentStatus
{
    ResourcesAssigned,
    NoResourcesAssigned,
    AssignmentInProgress,
}
