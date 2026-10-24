using Rebalancer.Core;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     PRB-0006: a client stopped with StopAsync reports <see cref="ClientState.Stopped" />, as documented, so
///     <see cref="RebalancerClient.BlockAsync" /> returns and the health check (ADR-0023) reports it Unhealthy. Redis
///     needs a server since 4.0: see the integration tests (<c>*PausedNodeTests.AStoppedClient_ReportsStopped</c>).
/// </summary>
public sealed class StoppedStateTests
{
    [Fact]
    public async Task AStoppedClient_ReportsStopped_WithNoResources()
    {
        using var client = await StartAssignedAsync();

        await client.StopAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ClientState.Stopped, client.GetCurrentState());
        var assigned = client.GetAssignedResources();
        Assert.Equal(ClientState.Stopped, assigned.ClientState);
        Assert.Empty(assigned.Resources);
    }

    [Fact]
    public async Task BlockAsync_ReturnsOnceTheClientIsStoppedElsewhere()
    {
        using var client = await StartAssignedAsync();
        var block = client.BlockAsync(CancellationToken.None, TimeSpan.FromSeconds(1));

        await client.StopAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            await Task.WhenAny(block, Task.Delay(TimeSpan.FromSeconds(5))) == block,
            $"BlockAsync still waits 5 s after StopAsync; the client reports {client.GetCurrentState()}"
        );
    }

    private static async Task<RebalancerClient> StartAssignedAsync()
    {
        var (provider, _) = SqlServerFollowerFakes.Create(
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero
        );
        var client = new RebalancerClient(provider);
        await client.StartAsync("orders", new ClientOptions());
        await SqlServerFollowerFakes.WaitUntilAsync(
            () => client.GetCurrentState() == ClientState.Assigned,
            "the follower was never assigned",
            TimeSpan.FromSeconds(10)
        );
        return client;
    }
}
