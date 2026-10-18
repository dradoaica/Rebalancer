using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.Core;

public interface IRebalancerProvider
{
    Task StartAsync(
        string group,
        OnChangeActions onChangeActions,
        CancellationToken token,
        ClientOptions clientOptions
    );

    Task RecreateClientAsync();
    Task WaitForCompletionAsync();

    /// <summary>
    ///     Returns the current assignment without waiting: while assignment is pending, or the client is not active, the
    ///     state says so and no resources are returned (ADR 0007).
    /// </summary>
    AssignedResources GetAssignedResources();

    ClientState GetState();
}
