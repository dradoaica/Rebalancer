using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Resources;
using Rebalancer.SqlServer.Roles;
using Rebalancer.SqlServer.Store;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract (ADR-0026, PRB-0005): a coordinator acts only while the lease names it with its fencing token, a new
///     tenure always rebalances, and a client that is stopped and not granted is assigned again. Mirrored for Redis
///     in <see cref="RedisCoordinatorLeaseTests" />.
/// </summary>
public sealed class SqlServerCoordinatorLeaseTests
{
    private const string Group = "group";
    private static readonly List<string> Resources = ["r1", "r2",];

    [Fact]
    public async Task ATickAfterTheLeaseWasTaken_StopsTheLocalHandlers()
    {
        var (clients, store, coordinator, actions, events, self) = await SingleCoordinatorAsync();
        await coordinator.ExecuteCoordinatorRoleAsync(self, Event(1), actions, CancellationToken.None);
        Assert.Equal("start:r1,r2", events.Last());

        clients.Lease = (Guid.NewGuid(), 2); // another node acquired the lease during a stall of this one
        var queuedEvent = Event(1);
        await coordinator.ExecuteCoordinatorRoleAsync(self, queuedEvent, actions, CancellationToken.None);

        Assert.Equal("stop", events.Last());
        Assert.True(queuedEvent.CoordinatorToken.FencingTokenViolation);
        Assert.Equal(AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
    }

    [Fact]
    public async Task AGrantAfterTheLeaseWasTaken_ChangesNoRecordAndStartsNothing()
    {
        var (clients, store, coordinator, actions, events, self) = await SingleCoordinatorAsync();
        var follower = Guid.NewGuid();
        await clients.CreateClientAsync(Group, follower);
        // a newer coordinator takes the lease while this one waits at the stop barrier
        clients.AfterStopActivity = () => clients.Lease = (Guid.NewGuid(), 2);

        var rebalanceEvent = Event(1);
        await coordinator.ExecuteCoordinatorRoleAsync(self, rebalanceEvent, actions, CancellationToken.None);

        Assert.DoesNotContain(events, x => x.StartsWith("start"));
        Assert.True(rebalanceEvent.CoordinatorToken.FencingTokenViolation);
        Assert.Equal(CoordinatorStatus.StopActivity, clients.Get(follower).CoordinatorStatus);
        Assert.Equal(CoordinatorStatus.StopActivity, clients.Get(self).CoordinatorStatus);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
        Assert.Equal(AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
    }

    [Fact]
    public async Task ANewFencingToken_ForcesARebalance()
    {
        var (clients, _, coordinator, actions, events, self) = await SingleCoordinatorAsync();
        await coordinator.ExecuteCoordinatorRoleAsync(self, Event(1), actions, CancellationToken.None);

        clients.Lease = (self, 2); // the same node acquired the lease again: same clients, same resources
        await coordinator.ExecuteCoordinatorRoleAsync(self, Event(2), actions, CancellationToken.None);

        // every rebalance first stops the coordinator's own handlers
        Assert.Equal(["stop", "start:r1,r2", "stop", "start:r1,r2",], events);
        Assert.Equal(2, clients.Get(self).FencingToken);
    }

    [Fact]
    public async Task AClientWaitingUnderStopActivity_IsAssignedAgain()
    {
        var (clients, _, coordinator, actions, _, self) = await SingleCoordinatorAsync();
        var follower = Guid.NewGuid();
        await clients.CreateClientAsync(Group, follower);
        await coordinator.ExecuteCoordinatorRoleAsync(self, Event(1), actions, CancellationToken.None);
        Assert.Equal(CoordinatorStatus.ResourcesGranted, clients.Get(follower).CoordinatorStatus);
        await clients.SetClientStatusAsync(follower, ClientStatus.Active); // the follower started its resources

        // the follower restarts after an error between two ticks: same client id, record reset (PRB-0005)
        await clients.ResetClientAsync(follower);
        await coordinator.ExecuteCoordinatorRoleAsync(self, Event(1), actions, CancellationToken.None);

        Assert.Equal(CoordinatorStatus.ResourcesGranted, clients.Get(follower).CoordinatorStatus);
        Assert.Single(clients.Get(follower).AssignedResources);
    }

    private static async Task<(FencedClients Clients, ResourceGroupStore Store, Coordinator Coordinator,
        OnChangeActions Actions, List<string> Events, Guid Self)> SingleCoordinatorAsync()
    {
        var clients = new FencedClients();
        var store = new ResourceGroupStore();
        var events = new List<string>();
        var actions = new OnChangeActions();
        actions.AddOnStartAction(resources => events.Add($"start:{string.Join(",", resources)}"));
        actions.AddOnStopAction(() => events.Add("stop"));
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);
        clients.Lease = (self, 1);
        var coordinator = new Coordinator(new NullRebalancerLogger(), new FixedResources(), clients, store);
        return (clients, store, coordinator, actions, events, self);
    }

    private static ClientEvent Event(int fencingToken) =>
        new()
        {
            ResourceGroup = Group,
            FencingToken = fencingToken,
            KeepAliveExpiryPeriod = TimeSpan.FromMinutes(1),
            CoordinatorToken = new CoordinatorToken(),
        };

    private sealed class FixedResources : IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(Resources.ToList());
    }

    /// <summary>
    ///     In-memory client table and lease with the semantics of the built-in client service, including the lease guard
    ///     (ADR-0026) and the record reset (PRB-0005).
    /// </summary>
    private sealed class FencedClients : IClientService, IFencedClientService
    {
        private readonly Dictionary<Guid, Client> clients = new();

        public (Guid CoordinatorId, int FencingToken) Lease { get; set; }

        // test hook, run after a successful StopActivity
        public Action? AfterStopActivity { get; set; }

        public Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            clients[clientId] = new Client
            {
                ClientId = clientId,
                ResourceGroup = resourceGroup,
                LastKeepAlive = DateTime.UtcNow,
                ClientStatus = ClientStatus.Waiting,
                CoordinatorStatus = CoordinatorStatus.StopActivity,
                FencingToken = 1,
            };
            return Task.CompletedTask;
        }

        public Task<List<Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(
                clients.Values
                    .Where(x => x.ResourceGroup == resourceGroup &&
                                x.ClientStatus is ClientStatus.Waiting or ClientStatus.Active
                    )
                    .Select(Copy)
                    .ToList()
            );

        public Task<Client> KeepAliveAsync(Guid clientId)
        {
            clients[clientId].LastKeepAlive = DateTime.UtcNow;
            return Task.FromResult(Copy(clients[clientId]));
        }

        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            clients[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> targets) =>
            StopActivityAsync(null!, Guid.Empty, fencingToken, targets);

        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests) =>
            StartActivityAsync(null!, Guid.Empty, fencingToken, requests);

        public Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken) =>
            Task.FromResult(Lease == (coordinatorClientId, fencingToken));

        public Task<ModifyClientResult> StopActivityAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            List<Client> targets
        )
        {
            var result = Modify(
                resourceGroup,
                coordinatorClientId,
                fencingToken,
                targets.Select(x => x.ClientId).ToList(),
                client =>
                {
                    client.CoordinatorStatus = CoordinatorStatus.StopActivity;
                    client.AssignedResources = [];
                }
            );
            if (result == ModifyClientResult.Ok)
            {
                AfterStopActivity?.Invoke();
            }

            return Task.FromResult(result);
        }

        public Task<ModifyClientResult> StartActivityAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            List<ClientStartRequest> requests
        ) =>
            Task.FromResult(
                Modify(
                    resourceGroup,
                    coordinatorClientId,
                    fencingToken,
                    requests.Select(x => x.ClientId).ToList(),
                    client =>
                    {
                        client.CoordinatorStatus = CoordinatorStatus.ResourcesGranted;
                        client.AssignedResources = requests.Single(x => x.ClientId == client.ClientId)
                            .AssignedResources.ToList();
                    }
                )
            );

        public Task ResetClientAsync(Guid clientId)
        {
            clients[clientId].ClientStatus = ClientStatus.Waiting;
            clients[clientId].CoordinatorStatus = CoordinatorStatus.StopActivity;
            clients[clientId].AssignedResources = [];
            return Task.CompletedTask;
        }

        public Client Get(Guid clientId) => clients[clientId];

        // all or nothing, like the built-in services; resourceGroup null: no lease guard
        private ModifyClientResult Modify(
            string? resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            List<Guid> ids,
            Action<Client> modify
        )
        {
            if ((resourceGroup != null && Lease != (coordinatorClientId, fencingToken)) ||
                ids.Any(x => !clients.TryGetValue(x, out var client) || client.FencingToken > fencingToken))
            {
                return ModifyClientResult.FencingTokenViolation;
            }

            foreach (var client in ids.Select(x => clients[x]))
            {
                modify(client);
                client.FencingToken = fencingToken;
            }

            return ModifyClientResult.Ok;
        }

        // the real services return deserialized copies, never live references
        private static Client Copy(Client x) =>
            new()
            {
                ClientId = x.ClientId,
                ResourceGroup = x.ResourceGroup,
                LastKeepAlive = x.LastKeepAlive,
                TimeNow = DateTime.UtcNow,
                ClientStatus = x.ClientStatus,
                CoordinatorStatus = x.CoordinatorStatus,
                FencingToken = x.FencingToken,
                AssignedResources = x.AssignedResources.ToList(),
            };
    }
}
