using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rebalancer.Core;
using Rebalancer.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>ADR-0023 / US-0018: the state-to-health mapping of a client's health check.</summary>
public sealed class RebalancerClientHealthCheckTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(2);
    private DateTime now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ClientState.Aborted)]
    [InlineData(ClientState.Stopped)]
    [InlineData(ClientState.NotStarted)]
    public async Task StatesThatTakeNoPart_AreUnhealthy(ClientState state)
    {
        var (check, _, _) = await StartedAsync(state);
        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(check)).Status);
    }

    [Fact]
    public async Task Assigned_IsHealthy()
    {
        var (check, _, _) = await StartedAsync(ClientState.Assigned);
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(check)).Status);
    }

    // the SQL Server and Redis providers keep reporting their last state after an abort
    [Fact]
    public async Task AnAbortWhileTheStateStillSaysAssigned_IsUnhealthy()
    {
        var (check, provider, _) = await StartedAsync(ClientState.Assigned);
        provider.Actions!.OnAbortActions[0]("fatal", new InvalidOperationException());
        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(check)).Status);
    }

    [Fact]
    public async Task PendingAssignment_IsDegradedOnlyAfterTheThreshold()
    {
        var (check, provider, _) = await StartedAsync(ClientState.Assigned);
        provider.State = ClientState.PendingAssignment;
        provider.Actions!.OnStopActions[0](); // unassigned now

        now += Threshold - TimeSpan.FromSeconds(1);
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(check)).Status);
        now += TimeSpan.FromSeconds(2);
        Assert.Equal(HealthStatus.Degraded, (await CheckAsync(check)).Status);

        provider.State = ClientState.Assigned;
        provider.Actions.OnStartActions[0](["r1"]);
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(check)).Status);
    }

    [Fact]
    public async Task TheCheckNeverCallsTheBackend()
    {
        var (check, provider, _) = await StartedAsync(ClientState.Assigned);
        await CheckAsync(check);
        Assert.Equal(0, provider.AssignedResourcesCalls);
    }

    [Fact]
    public async Task AddRebalancerClient_RegistersTheCheckForTheGivenClient()
    {
        var client = new RebalancerClient(new ScriptedProvider { State = ClientState.Assigned, });
        await client.StartAsync("g", new ClientOptions());
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // the check service logs
        services.AddHealthChecks().AddRebalancerClient("rebalancer", client);
        await using var serviceProvider = services.BuildServiceProvider();

        var report = await serviceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Entries["rebalancer"].Status);
    }

    private async Task<(RebalancerClientHealthCheck, ScriptedProvider, RebalancerClient)> StartedAsync(ClientState state)
    {
        var provider = new ScriptedProvider { State = state, };
        var client = new RebalancerClient(provider);
        var check = new RebalancerClientHealthCheck(client, Threshold, () => now);
        await client.StartAsync("g", new ClientOptions());
        return (check, provider, client);
    }

    private static Task<HealthCheckResult> CheckAsync(RebalancerClientHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext());

    // GetState is the only state source; GetAssignedResources stands for a call that reaches the backend
    private sealed class ScriptedProvider : IRebalancerProvider
    {
        public ClientState State { get; set; }
        public OnChangeActions? Actions { get; private set; }
        public int AssignedResourcesCalls { get; private set; }

        public Task StartAsync(string group, OnChangeActions onChangeActions, CancellationToken token, ClientOptions clientOptions)
        {
            Actions = onChangeActions;
            return Task.CompletedTask;
        }

        public Task RecreateClientAsync() => Task.CompletedTask;
        public Task WaitForCompletionAsync() => Task.CompletedTask;

        public AssignedResources GetAssignedResources()
        {
            AssignedResourcesCalls++;
            return new AssignedResources { ClientState = State, Resources = [], };
        }

        public ClientState GetState() => State;
    }
}
