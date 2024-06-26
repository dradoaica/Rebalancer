namespace Rebalancer.ZooKeeper;

public enum CoordinatorExitReason
{
    NoLongerCoordinator,
    Cancelled,
    SessionExpired,
    PotentialInconsistentState,
    FatalError,
}
