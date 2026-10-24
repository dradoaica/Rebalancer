using Rebalancer.Core;
using System.Diagnostics;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract (US-0003, ADR-0016): a disposable provider is released by Dispose, however the client was stopped
///     before; the StopAsync overloads and Dispose wait for the provider as documented; RecreateClientAsync reaches the
///     provider.
/// </summary>
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

    // US-0003: StopAsync() waits for the provider however long it takes
    [Fact]
    public async Task StopAsync_WithoutTimeout_WaitsForTheProvider()
    {
        var provider = new ControlledProvider();
        using var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());

        var stop = client.StopAsync();
        await Task.Delay(200);
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.False(stop.IsCompleted, "StopAsync() returned before the provider completed");

        provider.Complete();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // US-0003 criterion 1: StopAsync(timeout) returns after the timeout when the provider does not complete
    [Fact]
    public async Task StopAsync_WithTimeout_ReturnsAfterTheTimeout_WhenTheProviderHangs()
    {
        using var client = new RebalancerClient(new ControlledProvider());
        await client.StartAsync("group", new ClientOptions());

        await client.StopAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopAsync_WithTimeoutAndToken_ReturnsWhenTheTokenIsCancelled()
    {
        using var client = new RebalancerClient(new ControlledProvider());
        await client.StartAsync("group", new ClientOptions());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await client.StopAsync(TimeSpan.FromHours(1), cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // US-0003 criterion 4: Dispose cancels and waits at most 5 seconds for the provider
    [Fact]
    public async Task Dispose_WaitsAtMostFiveSeconds_ForAHangingProvider()
    {
        var provider = new ControlledProvider();
        var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());

        var elapsed = Stopwatch.StartNew();
        client.Dispose();

        Assert.True(provider.Token.IsCancellationRequested);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Dispose_DoesNotThrow_WhenTheProviderFaults()
    {
        var provider = new ControlledProvider();
        var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());
        provider.Fail(new InvalidOperationException("provider fault"));

        client.Dispose();
    }

    [Fact]
    public async Task RecreateClientAsync_DelegatesToTheProvider()
    {
        var provider = new ControlledProvider();
        using var client = new RebalancerClient(provider);
        await client.StartAsync("group", new ClientOptions());

        await client.RecreateClientAsync();

        Assert.Equal(1, provider.RecreateCount);
    }

    // a provider whose completion the test decides
    private sealed class ControlledProvider : IRebalancerProvider
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RecreateCount { get; private set; }
        public CancellationToken Token { get; private set; }

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

        public Task RecreateClientAsync()
        {
            RecreateCount++;
            return Task.CompletedTask;
        }

        public Task WaitForCompletionAsync() => completion.Task;

        public AssignedResources GetAssignedResources() => new()
        {
            Resources = [],
            ClientState = ClientState.Assigned,
        };

        public ClientState GetState() => ClientState.Assigned;
        public void Complete() => completion.TrySetResult();
        public void Fail(Exception exception) => completion.TrySetException(exception);
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
