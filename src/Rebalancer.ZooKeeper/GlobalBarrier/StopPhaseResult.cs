using System.Collections.Generic;
using Rebalancer.ZooKeeper.Zk;

namespace Rebalancer.ZooKeeper.GlobalBarrier;

public class StopPhaseResult
{
    public StopPhaseResult(RebalancingResult phaseResult)
    {
        PhaseResult = phaseResult;
    }

    public ClientsZnode ClientsZnode { get; set; }
    public ResourcesZnode ResourcesZnode { get; set; }
    public IList<string> FollowerIds { get; set; }
    public RebalancingResult PhaseResult { get; set; }
}
