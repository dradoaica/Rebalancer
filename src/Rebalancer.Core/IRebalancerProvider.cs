using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.Core;

public interface IRebalancerProvider
{
    Task StartAsync(string group, OnChangeActions onChangeActions, CancellationToken token,
        ClientOptions clientOptions);

    Task RecreateClientAsync();
    Task WaitForCompletionAsync();
    AssignedResources GetAssignedResources();
    ClientState GetState();
}
