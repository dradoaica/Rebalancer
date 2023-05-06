namespace Rebalancer.ZooKeeper;

using System.Threading.Tasks;

public interface IFollower
{
    Task<BecomeFollowerResult> BecomeFollowerAsync();
    Task<FollowerExitReason> StartEventLoopAsync();
}
