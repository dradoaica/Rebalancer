namespace Rebalancer.ZooKeeper.GlobalBarrier;

public enum FollowerEvent
{
    SessionExpired,
    IsNewLeader,
    RebalancingTriggered,
    PotentialInconsistentState,
    FatalError,
}
