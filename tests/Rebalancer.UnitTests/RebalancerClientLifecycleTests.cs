using Rebalancer.Core;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>Contract: a disposable provider is released by Dispose, however the client was stopped before.</summary>
public sealed class RebalancerClientLifecycleTests
{
    [Fact]
    public async Task Dispose_AfterStopAsync_DisposesProvider()
    {
        var provider = new FakeProvider();
        var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());

        await client.StopAsync();
        client.Dispose();

        Assert.Equal(1, provider.DisposeCount);
    }

    [Fact]
    public async Task Dispose_WithoutStop_StopsAndDisposesProviderOnce()
    {
        var provider = new FakeProvider();
        var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());

        client.Dispose();
        client.Dispose();

        Assert.True(provider.Token.IsCancellationRequested);
        Assert.Equal(1, provider.DisposeCount);
    }

    [Fact]
    public async Task StopAsync_AfterDispose_DoesNotThrow()
    {
        var client = new RebalancerClient(new FakeProvider());
        await client.StartAsync("group", new ClientOptions());

        client.Dispose();
        await client.StopAsync(TimeSpan.FromSeconds(1));
    }

    private sealed class FakeProvider : IRebalancerProvider, IDisposable
    {
        public int DisposeCount { get; private set; }
        public CancellationToken Token { get; private set; }

        public void Dispose() => DisposeCount++;

        public Task StartAsync(
            string group,
            OnChangeActions onChangeActions,
            CancellationToken token,
            ClientOptions clientOptions
        )
        {
            Token = token;
            return Task.CompletedTask;
        }

        public Task RecreateClientAsync() => Task.CompletedTask;
        public Task WaitForCompletionAsync() => Task.CompletedTask;

        public AssignedResources GetAssignedResources() => new()
        {
            Resources = [],
            ClientState = ClientState.Stopped,
        };

        public ClientState GetState() => ClientState.Stopped;
    }
}
