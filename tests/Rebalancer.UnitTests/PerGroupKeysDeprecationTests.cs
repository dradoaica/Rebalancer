using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Xunit;
using Redis = Rebalancer.Redis;
using SqlServer = Rebalancer.SqlServer;

namespace Rebalancer.UnitTests;

/// <summary>
///     US-0016 (ADR-0020): a Redis client warns once per session that 4.0 moves its group to per-group keys, also when it
///     restarts after errors; other backends do not.
/// </summary>
public sealed class PerGroupKeysDeprecationTests
{
    private static readonly ClientOptions RecoveringOptions = new()
    {
        AutoRecoveryOnError = true,
        RestartDelay = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public async Task Redis_WarnsOncePerSession_EvenAcrossRecoveryRestarts()
    {
        var logger = new WarningRecorder();
        var leases = new RedisFailingLeases();
        using var provider = new Redis.RedisProvider(
            "localhost:1,abortConnect=false,connectTimeout=100", logger, leases, null, new RedisClients()
        );

        await RunUntilAsync(provider, () => leases.Calls >= 3); // every lease call fails, so the client restarts

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("'orders'", warning);
        Assert.Contains("RBR:Lease:orders", warning);
        Assert.Contains("RBR:Resources:orders", warning);
    }

    [Fact]
    public async Task SqlServer_DoesNotWarn()
    {
        var logger = new WarningRecorder();
        var leases = new SqlServerFailingLeases();
        var provider = new SqlServer.SqlServerProvider(
            "Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;", logger, leases, null,
            new SqlServerClients()
        );

        await RunUntilAsync(provider, () => leases.Calls >= 1);

        Assert.Empty(logger.Warnings);
    }

    private static async Task RunUntilAsync(IRebalancerProvider provider, Func<bool> condition)
    {
        using var cts = new CancellationTokenSource();
        await provider.StartAsync("orders", new OnChangeActions(), cts.Token, RecoveringOptions);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await cts.CancelAsync();
        await provider.WaitForCompletionAsync();
        Assert.True(condition(), "the provider never reached the expected state");
    }

    private sealed class WarningRecorder : IRebalancerLogger
    {
        private readonly List<string> warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToList();
                }
            }
        }

        public void Warn(string clientId, string text)
        {
            lock (warnings)
            {
                warnings.Add(text);
            }
        }

        public void Debug(string clientId, string text) { }
        public void Info(string clientId, string text) { }
        public void Error(string clientId, string text) { }
        public void Error(string clientId, Exception ex) { }
        public void Error(string clientId, string text, Exception ex) { }
        public void SetMinimumLevel(LogLevel logLevel) { }
    }

    private sealed class RedisFailingLeases : Redis.Leases.ILeaseService
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<Redis.Leases.LeaseResponse> TryAcquireLeaseAsync(Redis.Leases.AcquireLeaseRequest request)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new Redis.Leases.LeaseResponse { Result = Redis.Leases.LeaseResult.Error });
        }

        public Task<Redis.Leases.LeaseResponse> TryRenewLeaseAsync(Redis.Leases.RenewLeaseRequest request) =>
            throw new NotSupportedException();

        public Task RelinquishLeaseAsync(Redis.Leases.RelinquishLeaseRequest request) => Task.CompletedTask;
    }

    private sealed class SqlServerFailingLeases : SqlServer.Leases.ILeaseService
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<SqlServer.Leases.LeaseResponse> TryAcquireLeaseAsync(SqlServer.Leases.AcquireLeaseRequest request)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new SqlServer.Leases.LeaseResponse { Result = SqlServer.Leases.LeaseResult.Error });
        }

        public Task<SqlServer.Leases.LeaseResponse> TryRenewLeaseAsync(SqlServer.Leases.RenewLeaseRequest request) =>
            throw new NotSupportedException();

        public Task RelinquishLeaseAsync(SqlServer.Leases.RelinquishLeaseRequest request) => Task.CompletedTask;
    }

    // registration and status changes succeed; nothing else is reached while the lease always fails
    private sealed class RedisClients : Redis.Clients.IClientService
    {
        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;
        public Task SetClientStatusAsync(Guid clientId, Redis.Clients.ClientStatus clientStatus) => Task.CompletedTask;
        public Task DeleteClientAsync(Guid clientId) => Task.CompletedTask;
        public Task<List<Redis.Clients.Client>> GetActiveClientsAsync(string resourceGroup) => throw new NotSupportedException();
        public Task<Redis.Clients.Client> KeepAliveAsync(Guid clientId) => throw new NotSupportedException();

        public Task<Redis.Clients.ModifyClientResult> StopActivityAsync(int fencingToken, List<Redis.Clients.Client> clients) =>
            throw new NotSupportedException();

        public Task<Redis.Clients.ModifyClientResult> StartActivityAsync(
            int fencingToken,
            List<Redis.Clients.ClientStartRequest> requests
        ) => throw new NotSupportedException();
    }

    private sealed class SqlServerClients : SqlServer.Clients.IClientService
    {
        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;
        public Task SetClientStatusAsync(Guid clientId, SqlServer.Clients.ClientStatus clientStatus) => Task.CompletedTask;
        public Task DeleteClientAsync(Guid clientId) => Task.CompletedTask;
        public Task<List<SqlServer.Clients.Client>> GetActiveClientsAsync(string resourceGroup) => throw new NotSupportedException();
        public Task<SqlServer.Clients.Client> KeepAliveAsync(Guid clientId) => throw new NotSupportedException();

        public Task<SqlServer.Clients.ModifyClientResult> StopActivityAsync(
            int fencingToken,
            List<SqlServer.Clients.Client> clients
        ) => throw new NotSupportedException();

        public Task<SqlServer.Clients.ModifyClientResult> StartActivityAsync(
            int fencingToken,
            List<SqlServer.Clients.ClientStartRequest> requests
        ) => throw new NotSupportedException();
    }
}
