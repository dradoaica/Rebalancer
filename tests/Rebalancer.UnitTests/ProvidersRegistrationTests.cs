using Rebalancer.Core;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract (ADR 0009): registering the same factory again is a no-op; registering a different one is rejected
///     instead of being silently ignored, and the first registration stays in effect.
/// </summary>
[Collection(nameof(ProvidersRegistrationTests))] // the registry is process-wide static state
public sealed class ProvidersRegistrationTests : IDisposable
{
    private static readonly IRebalancerProvider First = new StubProvider();
    private static readonly IRebalancerProvider Second = new StubProvider();

    public ProvidersRegistrationTests()
    {
        Providers.ResetForTests();
    }

    public void Dispose() => Providers.ResetForTests();

    [Fact]
    public void RegisteringTheSameFactoryAgain_IsANoOp()
    {
        Providers.Register(CreateFirst);
        Providers.Register(CreateFirst);

        Assert.Same(First, Providers.GetProvider());
    }

    [Fact]
    public void RegisteringADifferentFactory_Throws_AndKeepsTheFirst()
    {
        Providers.Register(CreateFirst);

        var ex = Assert.Throws<ProviderException>(() => Providers.Register(CreateSecond));
        Assert.Contains("RebalancerClient(IRebalancerProvider)", ex.Message);
        Assert.Same(First, Providers.GetProvider());
    }

    [Fact]
    public void WithoutRegistration_GetProviderStillThrows() => Assert.Throws<ProviderException>(Providers.GetProvider);

    private static IRebalancerProvider CreateFirst() => First;

    private static IRebalancerProvider CreateSecond() => Second;

    private sealed class StubProvider : IRebalancerProvider
    {
        public Task StartAsync(
            string group,
            OnChangeActions onChangeActions,
            CancellationToken token,
            ClientOptions clientOptions
        ) => Task.CompletedTask;

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
