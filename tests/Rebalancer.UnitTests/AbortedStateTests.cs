using Rebalancer.Core;
using Rebalancer.SqlServer;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Leases;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     US-0021: a SQL Server or Redis client that gives up after an error (no auto-recovery) reports
///     <see cref="ClientState.Aborted" />, as the ZooKeeper client does; with auto-recovery it keeps trying and does not.
/// </summary>
public sealed class AbortedStateTests
{
    // Redis needs a server since 4.0 (the provider checks the per-group key move at start): see
    // tests/Rebalancer.IntegrationTests/Redis/RedisAbortedStateTests.cs
    public static TheoryData<string> Backends => new()
    {
        "SqlServer",
    };

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task WithoutAutoRecovery_AFailedClientReportsAborted(string backend)
    {
        var (provider, calls) = Create(backend);
        using var client = new RebalancerClient(provider);
        var aborts = 0;
        client.OnAborted += (_, _) => Interlocked.Increment(ref aborts);

        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );
        await WaitUntilAsync(() => client.GetCurrentState() == ClientState.Aborted);

        Assert.Equal(1, calls());
        Assert.Equal(1, Volatile.Read(ref aborts));
        var assigned = client.GetAssignedResources();
        Assert.Equal(ClientState.Aborted, assigned.ClientState);
        Assert.Empty(assigned.Resources);
        // BlockAsync returns for an aborted client instead of waiting forever
        Assert.True(client.BlockAsync(CancellationToken.None, TimeSpan.FromSeconds(1)).Wait(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task WithAutoRecovery_AFailingClientKeepsTryingAndIsNotAborted(string backend)
    {
        var (provider, calls) = Create(backend);
        using var client = new RebalancerClient(provider);
        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromMilliseconds(10),
            }
        );
        await WaitUntilAsync(() => calls() >= 3);

        Assert.NotEqual(ClientState.Aborted, client.GetCurrentState());
        await client.StopAsync(TimeSpan.FromSeconds(5));
    }

    private static (IRebalancerProvider, Func<int>) Create(string backend)
    {
        var sqlLeases = new SqlServerFailingLeases();
        return (
            new SqlServerProvider(
                "Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;",
                null,
                sqlLeases,
                null,
                new SqlServerClients()
            ), () => sqlLeases.Calls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the expected state was not reached");
            await Task.Delay(20);
        }
    }

    private sealed class SqlServerFailingLeases : ILeaseService
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(
                new LeaseResponse
                {
                    Result = LeaseResult.Error,
                }
            );
        }

        public Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request) =>
            throw new NotSupportedException();

        public Task RelinquishLeaseAsync(RelinquishLeaseRequest request) => Task.CompletedTask;
    }

    private sealed class SqlServerClients : IClientService
    {
        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;
        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus) => Task.CompletedTask;
        public Task DeleteClientAsync(Guid clientId) => Task.CompletedTask;
        public Task<List<Client>> GetActiveClientsAsync(string resourceGroup) => throw new NotSupportedException();
        public Task<Client> KeepAliveAsync(Guid clientId) => throw new NotSupportedException();

        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
            throw new NotSupportedException();

        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests) =>
            throw new NotSupportedException();
    }
}
