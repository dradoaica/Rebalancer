using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Roles;
using Rebalancer.Redis.Store;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract: a coordinator that loses its lease while running its own resources must be stopped by the next
///     coordinator's stop barrier, exactly like a follower. Otherwise two nodes process the same resources.
/// </summary>
public sealed class CoordinatorDemotionTests
{
    private const string Group = "group";
    private static readonly List<string> Resources = ["r1", "r2",];

    [Fact]
    public async Task Redis_DemotedCoordinator_IsStoppedByNextCoordinator()
    {
        var clients = new RedisClients();
        var store = new ResourceGroupStore();
        var (actions, events) = RecordingActions();
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);

        // as coordinator with fencing token 1, assign all resources to itself
        await new Coordinator(new NullRebalancerLogger(), new RedisResources(), clients, store)
            .ExecuteCoordinatorRoleAsync(self, RedisEvent(1), actions, CancellationToken.None);
        Assert.Equal("start:r1,r2", events.Last());
        Assert.Equal(ClientStatus.Active, clients.Get(self).ClientStatus);

        // a newer coordinator (fencing token 2) requests every live client to stop, then this node follows
        await clients.StopActivityAsync(2, await clients.GetActiveClientsAsync(Group));
        await new Follower(new NullRebalancerLogger(), clients, store).ExecuteFollowerRoleAsync(
            self,
            RedisEvent(2),
            actions,
            CancellationToken.None
        );

        Assert.Equal("stop", events.Last());
        Assert.Equal(AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
    }

    [Fact]
    public async Task SqlServer_DemotedCoordinator_IsStoppedByNextCoordinator()
    {
        var clients = new SqlServerClients();
        var store = new SqlServer.Store.ResourceGroupStore();
        var (actions, events) = RecordingActions();
        var self = Guid.NewGuid();
        await clients.CreateClientAsync(Group, self);

        await new SqlServer.Roles.Coordinator(new NullRebalancerLogger(), new SqlServerResources(), clients, store)
            .ExecuteCoordinatorRoleAsync(self, SqlServerEvent(1), actions, CancellationToken.None);
        Assert.Equal("start:r1,r2", events.Last());
        Assert.Equal(SqlServer.Clients.ClientStatus.Active, clients.Get(self).ClientStatus);

        await clients.StopActivityAsync(2, await clients.GetActiveClientsAsync(Group));
        await new SqlServer.Roles.Follower(new NullRebalancerLogger(), clients, store).ExecuteFollowerRoleAsync(
            self,
            SqlServerEvent(2),
            actions,
            CancellationToken.None
        );

        Assert.Equal("stop", events.Last());
        Assert.Equal(SqlServer.Store.AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
        Assert.Equal(SqlServer.Clients.ClientStatus.Waiting, clients.Get(self).ClientStatus);
    }

    private static (OnChangeActions Actions, List<string> Events) RecordingActions()
    {
        var events = new List<string>();
        var actions = new OnChangeActions();
        actions.AddOnStartAction(resources => events.Add($"start:{string.Join(",", resources)}"));
        actions.AddOnStopAction(() => events.Add("stop"));
        return (actions, events);
    }

    private static ClientEvent RedisEvent(int fencingToken) =>
        new()
        {
            ResourceGroup = Group,
            FencingToken = fencingToken,
            KeepAliveExpiryPeriod = TimeSpan.FromMinutes(1),
            CoordinatorToken = new CoordinatorToken(),
        };

    private static SqlServer.Roles.ClientEvent SqlServerEvent(int fencingToken) =>
        new()
        {
            ResourceGroup = Group,
            FencingToken = fencingToken,
            KeepAliveExpiryPeriod = TimeSpan.FromMinutes(1),
            CoordinatorToken = new SqlServer.Roles.CoordinatorToken(),
        };

    private sealed class RedisResources : IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(Resources.ToList());
    }

    private sealed class SqlServerResources : SqlServer.Resources.IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(Resources.ToList());
    }

    /// <summary>In-memory client table with the same per-call semantics as the Redis client service.</summary>
    private sealed class RedisClients : IClientService
    {
        private readonly Dictionary<Guid, Client> clients = new();

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
                    .Select(x => Copy(x, DateTime.UtcNow))
                    .ToList()
            );

        public Task<Client> KeepAliveAsync(Guid clientId)
        {
            clients[clientId].LastKeepAlive = DateTime.UtcNow;
            return Task.FromResult(Copy(clients[clientId], DateTime.UtcNow));
        }

        public Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus)
        {
            clients[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> targets)
        {
            foreach (var client in targets.Select(x => clients[x.ClientId]))
            {
                client.CoordinatorStatus = CoordinatorStatus.StopActivity;
                client.AssignedResources = [];
                client.FencingToken = fencingToken;
            }

            return Task.FromResult(ModifyClientResult.Ok);
        }

        public Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> requests)
        {
            foreach (var request in requests)
            {
                var client = clients[request.ClientId];
                client.CoordinatorStatus = CoordinatorStatus.ResourcesGranted;
                client.AssignedResources = request.AssignedResources.ToList();
                client.FencingToken = fencingToken;
            }

            return Task.FromResult(ModifyClientResult.Ok);
        }

        public Client Get(Guid clientId) => clients[clientId];

        // the real services return deserialized copies, never live references
        private static Client Copy(Client x, DateTime now) =>
            new()
            {
                ClientId = x.ClientId,
                ResourceGroup = x.ResourceGroup,
                LastKeepAlive = x.LastKeepAlive,
                TimeNow = now,
                ClientStatus = x.ClientStatus,
                CoordinatorStatus = x.CoordinatorStatus,
                FencingToken = x.FencingToken,
                AssignedResources = x.AssignedResources.ToList(),
            };
    }

    /// <summary>In-memory client table with the same per-call semantics as the SQL Server client service.</summary>
    private sealed class SqlServerClients : SqlServer.Clients.IClientService
    {
        private readonly Dictionary<Guid, SqlServer.Clients.Client> clients = new();

        public Task CreateClientAsync(string resourceGroup, Guid clientId)
        {
            clients[clientId] = new SqlServer.Clients.Client
            {
                ClientId = clientId,
                ResourceGroup = resourceGroup,
                LastKeepAlive = DateTime.UtcNow,
                ClientStatus = SqlServer.Clients.ClientStatus.Waiting,
                CoordinatorStatus = SqlServer.Clients.CoordinatorStatus.StopActivity,
                FencingToken = 1,
            };
            return Task.CompletedTask;
        }

        public Task<List<SqlServer.Clients.Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(
                clients.Values
                    .Where(x => x.ResourceGroup == resourceGroup &&
                                x.ClientStatus is SqlServer.Clients.ClientStatus.Waiting
                                    or SqlServer.Clients.ClientStatus.Active
                    )
                    .Select(x => Copy(x, DateTime.UtcNow))
                    .ToList()
            );

        public Task<SqlServer.Clients.Client> KeepAliveAsync(Guid clientId)
        {
            clients[clientId].LastKeepAlive = DateTime.UtcNow;
            return Task.FromResult(Copy(clients[clientId], DateTime.UtcNow));
        }

        public Task SetClientStatusAsync(Guid clientId, SqlServer.Clients.ClientStatus clientStatus)
        {
            clients[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<SqlServer.Clients.ModifyClientResult> StopActivityAsync(
            int fencingToken,
            List<SqlServer.Clients.Client> targets
        )
        {
            foreach (var client in targets.Select(x => clients[x.ClientId]))
            {
                client.CoordinatorStatus = SqlServer.Clients.CoordinatorStatus.StopActivity;
                client.AssignedResources = [];
                client.FencingToken = fencingToken;
            }

            return Task.FromResult(SqlServer.Clients.ModifyClientResult.Ok);
        }

        public Task<SqlServer.Clients.ModifyClientResult> StartActivityAsync(
            int fencingToken,
            List<SqlServer.Clients.ClientStartRequest> requests
        )
        {
            foreach (var request in requests)
            {
                var client = clients[request.ClientId];
                client.CoordinatorStatus = SqlServer.Clients.CoordinatorStatus.ResourcesGranted;
                client.AssignedResources = request.AssignedResources.ToList();
                client.FencingToken = fencingToken;
            }

            return Task.FromResult(SqlServer.Clients.ModifyClientResult.Ok);
        }

        public SqlServer.Clients.Client Get(Guid clientId) => clients[clientId];

        private static SqlServer.Clients.Client Copy(SqlServer.Clients.Client x, DateTime now) =>
            new()
            {
                ClientId = x.ClientId,
                ResourceGroup = x.ResourceGroup,
                LastKeepAlive = x.LastKeepAlive,
                TimeNow = now,
                ClientStatus = x.ClientStatus,
                CoordinatorStatus = x.CoordinatorStatus,
                FencingToken = x.FencingToken,
                AssignedResources = x.AssignedResources.ToList(),
            };
    }
}
