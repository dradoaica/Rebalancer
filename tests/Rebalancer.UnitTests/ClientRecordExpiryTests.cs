using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Resources;
using Rebalancer.SqlServer.Roles;
using Rebalancer.SqlServer.Store;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of ADR-0014: the coordinator expires stale client records with its fencing token, the default
///     retention is 10 keep-alive expiry periods, and a node whose record has expired stops its local activity before it
///     re-registers, so it can never keep processing resources that were reassigned meanwhile.
/// </summary>
public sealed class ClientRecordExpiryTests
{
    private const string Group = "group";
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);

    [Fact]
    public void DefaultRetention_IsTenKeepAliveExpiryPeriods_AndCanBeConfigured()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ClientOptions.GetClientRecordRetention(new ClientOptions(), Expiry));
        Assert.Equal(
            TimeSpan.FromHours(2),
            ClientOptions.GetClientRecordRetention(
                new ClientOptions
                {
                    ClientRecordRetention = TimeSpan.FromHours(2),
                },
                Expiry
            )
        );
    }

    [Fact]
    public async Task SqlServer_Coordinator_ExpiresStaleRecordsWithItsFencingToken()
    {
        var clients = new SqlServerClients();
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);

        await new Coordinator(new NullRebalancerLogger(), new SqlServerNoResources(), clients, new ResourceGroupStore())
            .ExecuteCoordinatorRoleAsync(self, SqlServerEvent(7), new OnChangeActions(), CancellationToken.None);

        Assert.Equal((Group, self, 7, TimeSpan.FromMinutes(10)), Assert.Single(clients.ExpiryRequests));
    }

    [Fact]
    public async Task Redis_Coordinator_ExpiresStaleRecordsWithItsFencingToken()
    {
        var clients = new RedisClients();
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);

        await new Redis.Roles.Coordinator(
            new NullRebalancerLogger(),
            new RedisNoResources(),
            clients,
            new Redis.Store.ResourceGroupStore()
        ).ExecuteCoordinatorRoleAsync(self, RedisEvent(7), new OnChangeActions(), CancellationToken.None);

        Assert.Equal((Group, self, 7, TimeSpan.FromMinutes(10)), Assert.Single(clients.ExpiryRequests));
    }

    [Fact]
    public async Task SqlServer_CoordinatorKeepsCoordinating_WhenExpiryFails()
    {
        var clients = new SqlServerClients
        {
            FailExpiry = true,
        }; // e.g. a SQL login without DELETE
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);
        var store = new ResourceGroupStore();

        await new Coordinator(new NullRebalancerLogger(), new SqlServerNoResources(), clients, store)
            .ExecuteCoordinatorRoleAsync(self, SqlServerEvent(7), new OnChangeActions(), CancellationToken.None);

        Assert.Single(clients.ExpiryRequests);
        Assert.Equal(
            AssignmentStatus.ResourcesAssigned,
            store.GetResources().AssignmentStatus
        ); // rebalancing completed
    }

    [Fact]
    public async Task SqlServer_FollowerWithExpiredRecord_StopsBeforeReRegistering()
    {
        var clients = new SqlServerClients();
        var store = new ResourceGroupStore();
        store.SetResources(
            new SetResourcesRequest
            {
                AssignmentStatus = AssignmentStatus.ResourcesAssigned,
                Resources = ["r1",],
            }
        );
        var (actions, events) = RecordingActions(clients.Log);
        var self = Guid.NewGuid(); // no record: it expired while this node was paused

        await new Follower(new NullRebalancerLogger(), clients, store).ExecuteFollowerRoleAsync(
            self,
            SqlServerEvent(1),
            actions,
            CancellationToken.None
        );

        Assert.Equal(["stop", $"create:{self}",], clients.Log.Take(2));
        Assert.Equal(AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
        Assert.DoesNotContain(events, x => x.StartsWith("start"));
    }

    [Fact]
    public async Task Redis_FollowerWithExpiredRecord_StopsBeforeReRegistering()
    {
        var clients = new RedisClients();
        var store = new Redis.Store.ResourceGroupStore();
        store.SetResources(
            new Redis.Store.SetResourcesRequest
            {
                AssignmentStatus = Redis.Store.AssignmentStatus.ResourcesAssigned,
                Resources = ["r1",],
            }
        );
        var (actions, events) = RecordingActions(clients.Log);
        var self = Guid.NewGuid();

        await new Redis.Roles.Follower(new NullRebalancerLogger(), clients, store).ExecuteFollowerRoleAsync(
            self,
            RedisEvent(1),
            actions,
            CancellationToken.None
        );

        Assert.Equal(["stop", $"create:{self}",], clients.Log.Take(2));
        Assert.Equal(Redis.Store.AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
        Assert.Equal(Redis.Clients.ClientStatus.Waiting, clients.Get(self).ClientStatus);
        Assert.DoesNotContain(events, x => x.StartsWith("start"));
    }

    [Fact]
    public async Task FollowerWithExpiredRecord_ThatWasNotStarted_IsNotSentAStop()
    {
        var clients = new SqlServerClients();
        var (actions, events) = RecordingActions(clients.Log);
        var self = Guid.NewGuid();

        await new Follower(new NullRebalancerLogger(), clients, new ResourceGroupStore()).ExecuteFollowerRoleAsync(
            self,
            SqlServerEvent(1),
            actions,
            CancellationToken.None
        );

        Assert.Empty(events);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
    }

    private static (OnChangeActions, List<string>) RecordingActions(List<string> sharedLog)
    {
        var events = new List<string>();
        var actions = new OnChangeActions();
        actions.AddOnStartAction(r =>
            {
                events.Add($"start:{string.Join(",", r)}");
                sharedLog.Add("start");
            }
        );
        actions.AddOnStopAction(() =>
            {
                events.Add("stop");
                sharedLog.Add("stop");
            }
        );
        return (actions, events);
    }

    private static ClientEvent SqlServerEvent(int fencingToken) =>
        new()
        {
            ResourceGroup = Group,
            FencingToken = fencingToken,
            KeepAliveExpiryPeriod = Expiry,
            ClientRecordRetention = ClientOptions.GetClientRecordRetention(new ClientOptions(), Expiry),
            CoordinatorToken = new CoordinatorToken(),
        };

    private static Redis.Roles.ClientEvent RedisEvent(int fencingToken) =>
        new()
        {
            ResourceGroup = Group,
            FencingToken = fencingToken,
            KeepAliveExpiryPeriod = Expiry,
            ClientRecordRetention = ClientOptions.GetClientRecordRetention(new ClientOptions(), Expiry),
            CoordinatorToken = new Redis.Roles.CoordinatorToken(),
        };

    private sealed class SqlServerNoResources : IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(new List<string>());
    }

    private sealed class RedisNoResources : Redis.Resources.IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(new List<string>());
    }

    /// <summary>In-memory client table; a missing record behaves like the built-in service (ClientNotFoundException).</summary>
    private sealed class SqlServerClients : IClientService
    {
        private readonly Dictionary<Guid, Client> clients = new();
        public List<string> Log { get; } = [];
        public List<(string, Guid, int, TimeSpan)> ExpiryRequests { get; } = [];

        public bool FailExpiry { get; init; }

        public Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            Log.Add($"create:{clientId}");
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

        public Task<Client> KeepAliveAsync(Guid clientId) =>
            clients.TryGetValue(clientId, out var c) ? Task.FromResult(c) : throw new ClientNotFoundException(clientId);

        public Task<List<Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(new List<Client>());

        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            clients[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> c) =>
            Task.FromResult(ModifyClientResult.Ok);

        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> r) =>
            Task.FromResult(ModifyClientResult.Ok);

        public Task<int> DeleteExpiredClientsAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            TimeSpan retention
        )
        {
            ExpiryRequests.Add((resourceGroup, coordinatorClientId, fencingToken, retention));
            return FailExpiry ? throw new InvalidOperationException("DELETE permission denied") : Task.FromResult(0);
        }

        public Client Get(Guid id) => clients[id];
    }

    private sealed class RedisClients : Redis.Clients.IClientService
    {
        private readonly Dictionary<Guid, Redis.Clients.Client> clients = new();
        public List<string> Log { get; } = [];
        public List<(string, Guid, int, TimeSpan)> ExpiryRequests { get; } = [];

        public Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            Log.Add($"create:{clientId}");
            clients[clientId] = new Redis.Clients.Client
            {
                ClientId = clientId,
                ResourceGroup = resourceGroup,
                LastKeepAlive = DateTime.UtcNow,
                ClientStatus = Redis.Clients.ClientStatus.Waiting,
                CoordinatorStatus = Redis.Clients.CoordinatorStatus.StopActivity,
                FencingToken = 1,
            };
            return Task.CompletedTask;
        }

        public Task<Redis.Clients.Client> KeepAliveAsync(Guid clientId) =>
            clients.TryGetValue(clientId, out var c) ? Task.FromResult(c)
                : throw new Redis.Clients.ClientNotFoundException(clientId);

        public Task<List<Redis.Clients.Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(new List<Redis.Clients.Client>());

        public Task SetClientStatusAsync(Guid clientId, Redis.Clients.ClientStatus clientStatus)
        {
            clients[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<Redis.Clients.ModifyClientResult> StopActivityAsync(
            int fencingToken,
            List<Redis.Clients.Client> c
        ) =>
            Task.FromResult(Redis.Clients.ModifyClientResult.Ok);

        public Task<Redis.Clients.ModifyClientResult> StartActivityAsync(
            int fencingToken,
            List<Redis.Clients.ClientStartRequest> r
        ) =>
            Task.FromResult(Redis.Clients.ModifyClientResult.Ok);

        public Task<int> DeleteExpiredClientsAsync(
            string resourceGroup,
            Guid coordinatorClientId,
            int fencingToken,
            TimeSpan retention
        )
        {
            ExpiryRequests.Add((resourceGroup, coordinatorClientId, fencingToken, retention));
            return Task.FromResult(0);
        }

        public Redis.Clients.Client Get(Guid id) => clients[id];
    }
}
