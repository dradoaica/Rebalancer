using Rebalancer.Redis;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Leases;
using Rebalancer.Redis.Resources;
using StackExchange.Redis;

namespace Rebalancer.IntegrationTests.Redis;

/// <summary>
///     A Redis provider whose store calls wait while the given <see cref="NodePause" /> is paused. The services use
///     the given database; the provider still opens (and owns) its own connection.
/// </summary>
internal static class PausableRedisServices
{
    public static RedisProvider CreateProvider(string connectionString, IDatabase db, NodePause pause) =>
        new(
            connectionString,
            leaseService: new PausableLeaseService(new LeaseService(db), pause),
            resourceService: new PausableResourceService(new ResourceService(db), pause),
            clientService: new PausableClientService(new ClientService(db), pause)
        );

    private sealed class PausableLeaseService(ILeaseService inner, NodePause pause) : ILeaseService
    {
        public async Task<LeaseResponse> TryAcquireLeaseAsync(AcquireLeaseRequest request)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.TryAcquireLeaseAsync(request);
        }

        public async Task<LeaseResponse> TryRenewLeaseAsync(RenewLeaseRequest request)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.TryRenewLeaseAsync(request);
        }

        public async Task RelinquishLeaseAsync(RelinquishLeaseRequest request)
        {
            await pause.WaitWhilePausedAsync();
            await inner.RelinquishLeaseAsync(request);
        }
    }

    private sealed class PausableResourceService(IResourceService inner, NodePause pause) : IResourceService
    {
        public async Task<List<string>> GetResourcesAsync(string resourceGroup)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.GetResourcesAsync(resourceGroup);
        }
    }

    // forwards every member, including the ones with default implementations (ADR-0014), so the node behaves as the
    // built-in service does
    private sealed class PausableClientService(IClientService inner, NodePause pause)
        : IClientService, IFencedClientService
    {
        private IFencedClientService Fenced => (IFencedClientService)inner;

        public async Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            await pause.WaitWhilePausedAsync();
            await inner.CreateClientAsync(resourceGroup, clientId);
        }

        public async Task<List<Client>> GetActiveClientsAsync(string resourceGroup)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.GetActiveClientsAsync(resourceGroup);
        }

        public async Task<Client> KeepAliveAsync(Guid clientId)
        {
            var afterResume = await pause.WaitWhilePausedAsync();
            pause.ThrowIfKeepAliveFailureRequested();
            var self = await inner.KeepAliveAsync(clientId);
            pause.RecordKeepAlive(afterResume, self.CoordinatorStatus == CoordinatorStatus.StopActivity);
            return self;
        }

        public async Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            await pause.WaitWhilePausedAsync();
            await inner.SetClientStatusAsync(clientId, clientStatus);
        }

        public async Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.StopActivityAsync(fencingToken, clients);
        }

        public async Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests)
        {
            await pause.WaitWhilePausedAsync();
            return await inner.StartActivityAsync(fencingToken, requests);
        }

        public async Task<int> DeleteExpiredClientsAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            TimeSpan retention
        )
        {
            await pause.WaitWhilePausedAsync();
            return await inner.DeleteExpiredClientsAsync(resourceGroup, coordinatorClientId, fencingToken, retention);
        }

        public async Task DeleteClientAsync(Guid clientId)
        {
            await pause.WaitWhilePausedAsync();
            await inner.DeleteClientAsync(clientId);
        }

        // ADR-0026 / PRB-0005: the built-in service's internal operations, so the node behaves as it does in production
        public async Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken)
        {
            await pause.WaitWhilePausedAsync();
            return await Fenced.HoldsLeaseAsync(resourceGroup, coordinatorClientId, fencingToken);
        }

        public async Task<ModifyClientResult> StopActivityAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            List<Client> clients
        )
        {
            await pause.WaitWhilePausedAsync();
            return await Fenced.StopActivityAsync(resourceGroup, coordinatorClientId, fencingToken, clients);
        }

        public async Task<ModifyClientResult> StartActivityAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            List<ClientStartRequest> requests
        )
        {
            await pause.WaitWhilePausedAsync();
            return await Fenced.StartActivityAsync(resourceGroup, coordinatorClientId, fencingToken, requests);
        }

        public async Task ResetClientAsync(Guid clientId)
        {
            await pause.WaitWhilePausedAsync();
            await Fenced.ResetClientAsync(clientId);
        }
    }
}
