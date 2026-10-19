using Rebalancer.Core;
using Rebalancer.Redis;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Leases;
using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     Requires Docker. US-0021 for Redis: a client that gives up after an error reports Aborted; with auto-recovery it
///     does not. The lease service always fails; the real server is only needed for the provider's start-up checks.
/// </summary>
public sealed class RedisAbortedStateTests
{
    [SkippableFact]
    public async Task WithoutAutoRecovery_AFailedClientReportsAborted()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var leases = new FailingLeases();
        using var client = new RebalancerClient(new RedisProvider(redis.GetConnectionString(), null, leases, null, new NoopClients()));

        await client.StartAsync("orders", new ClientOptions { AutoRecoveryOnError = false, });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (client.GetCurrentState() != ClientState.Aborted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(ClientState.Aborted, client.GetCurrentState());
        Assert.Equal(ClientState.Aborted, client.GetAssignedResources().ClientState);
        Assert.Equal(1, leases.Calls);
    }

    [SkippableFact]
    public async Task WithAutoRecovery_AFailingClientIsNotAborted()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        await using var redis = new RedisBuilder(DockerSupport.RedisImage).Build();
        await redis.StartAsync();
        var leases = new FailingLeases();
        using var client = new RebalancerClient(new RedisProvider(redis.GetConnectionString(), null, leases, null, new NoopClients()));

        await client.StartAsync("orders", new ClientOptions { AutoRecoveryOnError = true, RestartDelay = TimeSpan.FromMilliseconds(10), });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (leases.Calls < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(leases.Calls >= 3, "the client did not keep trying");
        Assert.NotEqual(ClientState.Aborted, client.GetCurrentState());
        await client.StopAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FailingLeases : ILeaseService
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new LeaseResponse { Result = LeaseResult.Error, });
        }

        public Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request) => throw new NotSupportedException();
        public Task RelinquishLeaseAsync(RelinquishLeaseRequest request) => Task.CompletedTask;
    }

    // registration and status changes succeed; nothing else is reached while the lease always fails
    private sealed class NoopClients : IClientService
    {
        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;
        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus) => Task.CompletedTask;
        public Task DeleteClientAsync(Guid clientId) => Task.CompletedTask;
        public Task<List<Client>> GetActiveClientsAsync(string resourceGroup) => throw new NotSupportedException();
        public Task<Client> KeepAliveAsync(Guid clientId) => throw new NotSupportedException();
        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) => throw new NotSupportedException();
        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests) => throw new NotSupportedException();
    }
}
