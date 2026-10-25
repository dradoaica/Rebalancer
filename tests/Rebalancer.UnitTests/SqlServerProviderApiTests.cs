using Rebalancer.Core;
using Rebalancer.SqlServer;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Public members of the SQL Server provider that only the provider offers (not
///     <see cref="IRebalancerProvider" />): <see cref="SqlServerProvider.RecreateClientAsync" /> and the waiting
///     <see cref="SqlServerProvider.GetAssignedResources(CancellationToken)" /> (ADR-0007). The Redis provider has the
///     same members on the same code and needs a server to start (see <see cref="AbortedStateTests" />).
/// </summary>
public sealed class SqlServerProviderApiTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task RecreateClientAsync_BeforeStart_Throws()
    {
        var (provider, _) = SqlServerFollowerFakes.Create(Expiry, Heartbeat, TimeSpan.Zero);

        await Assert.ThrowsAsync<RebalancerException>(provider.RecreateClientAsync);
    }

    [Fact]
    public async Task RecreateClientAsync_AfterStart_RegistersTheClientAgain()
    {
        var (provider, clients) = SqlServerFollowerFakes.Create(Expiry, Heartbeat, TimeSpan.Zero);
        using var client = new RebalancerClient(provider);
        await client.StartAsync("orders", new ClientOptions());
        var before = clients.Creates;

        await client.RecreateClientAsync();

        Assert.Equal(before + 1, clients.Creates);
        await client.StopAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetAssignedResourcesWithToken_WaitsForTheAssignment()
    {
        var (provider, _) = SqlServerFollowerFakes.Create(Expiry, Heartbeat, TimeSpan.FromSeconds(1));
        using var client = new RebalancerClient(provider);
        await client.StartAsync("orders", new ClientOptions());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var assigned = await Task.Run(() => provider.GetAssignedResources(cts.Token));

        Assert.Equal(ClientState.Assigned, assigned.ClientState);
        Assert.Equal(SqlServerFollowerFakes.GrantingClients.Resources, assigned.Resources);
        await client.StopAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetAssignedResourcesWithToken_ReturnsStopped_WhenCancelledBeforeAnAssignment()
    {
        var (provider, _) = SqlServerFollowerFakes.Create(Expiry, Heartbeat, TimeSpan.FromHours(1));
        using var client = new RebalancerClient(provider);
        await client.StartAsync("orders", new ClientOptions());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // cts outlives the call unless WaitAsync times out, and then the test has already failed
        // ReSharper disable once AccessToDisposedClosure
        var assigned = await Task.Run(() => provider.GetAssignedResources(cts.Token))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ClientState.Stopped, assigned.ClientState);
        Assert.Empty(assigned.Resources);
        await client.StopAsync(TimeSpan.FromSeconds(5));
    }
}
