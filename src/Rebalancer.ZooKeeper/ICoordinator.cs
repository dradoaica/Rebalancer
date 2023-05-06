namespace Rebalancer.ZooKeeper;

using System.Threading.Tasks;

public interface ICoordinator
{
    Task<BecomeCoordinatorResult> BecomeCoordinatorAsync(int currentEpoch);
    Task<CoordinatorExitReason> StartEventLoopAsync();
}
