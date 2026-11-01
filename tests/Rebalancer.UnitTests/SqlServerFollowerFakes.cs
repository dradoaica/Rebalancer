using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.SqlServer;
using System.Diagnostics;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     A SQL Server node that is always a follower (the lease is denied) and that another coordinator grants
///     <see cref="GrantingClients.Resources" />, from <see cref="GrantingClients.GrantAfter" /> after the first keep-alive
///     (until <c>revokeAfter</c>, when given, after which it records a stop). Records the time of every keep-alive.
/// </summary>
internal static class SqlServerFollowerFakes
{
    public static (SqlServerProvider Provider, GrantingClients Clients) Create(
        TimeSpan keepAliveExpiry,
        TimeSpan heartbeat,
        TimeSpan grantAfter,
        TimeSpan? revokeAfter = null
    )
    {
        var clients = new GrantingClients(grantAfter, revokeAfter);
        var provider = new SqlServerProvider(
            "Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;",
            null,
            new DeniedLeases(keepAliveExpiry, heartbeat),
            null,
            clients
        );
        return (provider, clients);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string failure, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(20);
        }
    }

    private sealed class DeniedLeases(TimeSpan expiry, TimeSpan heartbeat) : ILeaseService
    {
        public Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request) =>
            Task.FromResult(
                new LeaseResponse
                {
                    Result = LeaseResult.Denied,
                    Lease = new Lease
                    {
                        ResourceGroup = request.ResourceGroup,
                        ClientId = Guid.NewGuid(),
                        FencingToken = 1,
                        ExpiryPeriod = expiry,
                        HeartbeatPeriod = heartbeat,
                    },
                }
            );

        public Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request) => throw new NotSupportedException();
        public Task RelinquishLeaseAsync(RelinquishLeaseRequest request) => Task.CompletedTask;
    }

    internal sealed class GrantingClients(TimeSpan grantAfter, TimeSpan? revokeAfter) : IClientService
    {
        public static readonly List<string> Resources = ["r1",];
        private readonly List<TimeSpan> keepAlives = [];
        private readonly Stopwatch since = new();
        private readonly object sync = new();
        private int creates;
        private ClientStatus status = ClientStatus.Waiting;

        /// <summary>How many times the node registered (CreateClientAsync).</summary>
        public int Creates => Volatile.Read(ref creates);

        public TimeSpan GrantAfter => grantAfter;

        /// <summary>Time of each keep-alive, from the first one.</summary>
        public IReadOnlyList<TimeSpan> KeepAlives
        {
            get
            {
                lock (sync)
                {
                    return keepAlives.ToList();
                }
            }
        }

        /// <summary>Time from the first keep-alive until now (the clock the grant is measured on).</summary>
        public TimeSpan Elapsed => since.Elapsed;

        public Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            Interlocked.Increment(ref creates);
            return Task.CompletedTask;
        }

        public Task DeleteClientAsync(Guid clientId) => Task.CompletedTask;
        public Task<List<Client>> GetActiveClientsAsync(string resourceGroup) => throw new NotSupportedException();

        public Task<Client> KeepAliveAsync(Guid clientId)
        {
            lock (sync)
            {
                if (!since.IsRunning)
                {
                    since.Start();
                }

                keepAlives.Add(since.Elapsed);
                var granted = since.Elapsed >= grantAfter && !(since.Elapsed >= revokeAfter);
                return Task.FromResult(
                    new Client
                    {
                        ClientId = clientId,
                        ClientStatus = status,
                        CoordinatorStatus =
                            granted ? CoordinatorStatus.ResourcesGranted : CoordinatorStatus.StopActivity,
                        AssignedResources = granted ? Resources.ToList() : [],
                        FencingToken = 1,
                    }
                );
            }
        }

        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            lock (sync)
            {
                status = clientStatus;
            }

            return Task.CompletedTask;
        }

        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients) =>
            throw new NotSupportedException();

        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests) =>
            throw new NotSupportedException();
    }
}
