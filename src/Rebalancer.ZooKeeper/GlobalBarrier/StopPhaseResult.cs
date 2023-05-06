namespace Rebalancer.ZooKeeper.GlobalBarrier;

using System.Collections.Generic;
using Zk;

public class StopPhaseResult
{
    public StopPhaseResult(RebalancingResult phaseResult) => this.PhaseResult = phaseResult;

    public ClientsZnode ClientsZnode { get; set; }
    public ResourcesZnode ResourcesZnode { get; set; }
    public IList<string> FollowerIds { get; set; }
    public RebalancingResult PhaseResult { get; set; }
}
