using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Xunit;
using Redis = Rebalancer.Redis;
using SqlServer = Rebalancer.SqlServer;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of ADR-0019 (PRB-0001): when the coordinator reassigns the resources of a client whose keep-alive has
///     expired, it also tells that client to stop, so a resumed follower stops at its first tick and never starts again
///     with the reassigned resources.
/// </summary>
public sealed class UnresponsiveFollowerTests
{
    private const string Group = "g";
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task SqlServer_ResumedFollower_StopsAndDoesNotRestartWithReassignedResources()
    {
        var table = new SqlServerTable();
        var coordinator = Guid.NewGuid();
        var follower = Guid.NewGuid();
        table.Add(coordinator, true, [], DateTime.UtcNow);
        table.Add(follower, true, ["r1"], DateTime.UtcNow.AddMinutes(-5)); // paused: keep-alive older than the expiry

        await new SqlServer.Roles.Coordinator(new NullRebalancerLogger(), new SqlServerR1(), table, new())
            .ExecuteCoordinatorRoleAsync(coordinator, SqlServerEvent(2), new OnChangeActions(), CancellationToken.None);

        Assert.Contains("r1", table.Rows[coordinator].AssignedResources);
        Assert.Equal(SqlServer.Clients.CoordinatorStatus.StopActivity, table.Rows[follower].CoordinatorStatus);
        Assert.Empty(table.Rows[follower].AssignedResources);

        var (actions, events) = Recording();
        var store = new SqlServer.Store.ResourceGroupStore();
        store.SetResources(new() { AssignmentStatus = SqlServer.Store.AssignmentStatus.ResourcesAssigned, Resources = ["r1"], });
        await new SqlServer.Roles.Follower(new NullRebalancerLogger(), table, store)
            .ExecuteFollowerRoleAsync(follower, SqlServerEvent(2), actions, CancellationToken.None);

        Assert.Equal(["stop"], events);
        Assert.Equal(SqlServer.Store.AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);
    }

    [Fact]
    public async Task Redis_ResumedFollower_StopsAndDoesNotRestartWithReassignedResources()
    {
        var table = new RedisTable();
        var coordinator = Guid.NewGuid();
        var follower = Guid.NewGuid();
        table.Add(coordinator, true, [], DateTime.UtcNow);
        table.Add(follower, true, ["r1"], DateTime.UtcNow.AddMinutes(-5));

        await new Redis.Roles.Coordinator(new NullRebalancerLogger(), new RedisR1(), table, new())
            .ExecuteCoordinatorRoleAsync(coordinator, RedisEvent(2), new OnChangeActions(), CancellationToken.None);

        Assert.Contains("r1", table.Rows[coordinator].AssignedResources);
        Assert.Equal(Redis.Clients.CoordinatorStatus.StopActivity, table.Rows[follower].CoordinatorStatus);

        var (actions, events) = Recording();
        var store = new Redis.Store.ResourceGroupStore();
        store.SetResources(new() { AssignmentStatus = Redis.Store.AssignmentStatus.ResourcesAssigned, Resources = ["r1"], });
        await new Redis.Roles.Follower(new NullRebalancerLogger(), table, store)
            .ExecuteFollowerRoleAsync(follower, RedisEvent(2), actions, CancellationToken.None);

        Assert.Equal(["stop"], events);
    }

    [Fact]
    public async Task UnresponsiveClientWithoutAssignment_IsNotWrittenAgain()
    {
        var table = new SqlServerTable();
        var coordinator = Guid.NewGuid();
        var idle = Guid.NewGuid();
        table.Add(coordinator, true, [], DateTime.UtcNow);
        table.Add(idle, false, [], DateTime.UtcNow.AddMinutes(-5)); // StopActivity already, nothing to take back

        await new SqlServer.Roles.Coordinator(new NullRebalancerLogger(), new SqlServerR1(), table, new())
            .ExecuteCoordinatorRoleAsync(coordinator, SqlServerEvent(2), new OnChangeActions(), CancellationToken.None);

        Assert.DoesNotContain(table.StopRequests, ids => ids.Contains(idle));
    }

    [Fact]
    public async Task StopOfUnresponsiveClientsReportingAViolation_DoesNotAbortTheRebalance()
    {
        // e.g. a SQL Server row deleted between the read and the stop: fewer rows updated than requested
        var table = new SqlServerTable { ViolateStopFor = 0 }; // no live followers: the first stop call is the one for unresponsive clients
        var coordinator = Guid.NewGuid();
        var follower = Guid.NewGuid();
        table.Add(coordinator, true, [], DateTime.UtcNow);
        table.Add(follower, true, ["r1"], DateTime.UtcNow.AddMinutes(-5));
        var evt = SqlServerEvent(2);

        await new SqlServer.Roles.Coordinator(new NullRebalancerLogger(), new SqlServerR1(), table, new())
            .ExecuteCoordinatorRoleAsync(coordinator, evt, new OnChangeActions(), CancellationToken.None);

        Assert.False(evt.CoordinatorToken.FencingTokenViolation);
        Assert.Contains("r1", table.Rows[coordinator].AssignedResources); // the rebalance completed
    }

    private static (OnChangeActions, List<string>) Recording()
    {
        var events = new List<string>();
        var actions = new OnChangeActions();
        actions.AddOnStartAction(r => events.Add($"start:{string.Join(",", r)}"));
        actions.AddOnStopAction(() => events.Add("stop"));
        return (actions, events);
    }

    private static SqlServer.Roles.ClientEvent SqlServerEvent(int token) =>
        new()
        {
            ResourceGroup = Group, FencingToken = token, KeepAliveExpiryPeriod = Expiry,
            ClientRecordRetention = TimeSpan.FromHours(1), CoordinatorToken = new SqlServer.Roles.CoordinatorToken(),
        };

    private static Redis.Roles.ClientEvent RedisEvent(int token) =>
        new()
        {
            ResourceGroup = Group, FencingToken = token, KeepAliveExpiryPeriod = Expiry,
            ClientRecordRetention = TimeSpan.FromHours(1), CoordinatorToken = new Redis.Roles.CoordinatorToken(),
        };

    private sealed class SqlServerR1 : SqlServer.Resources.IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(new List<string> { "r1" });
    }

    private sealed class RedisR1 : Redis.Resources.IResourceService
    {
        public Task<List<string>> GetResourcesAsync(string resourceGroup) => Task.FromResult(new List<string> { "r1" });
    }

    /// <summary>In-memory client table with the SQL Server service's per-call semantics (copies, not live references).</summary>
    private sealed class SqlServerTable : SqlServer.Clients.IClientService
    {
        public Dictionary<Guid, SqlServer.Clients.Client> Rows { get; } = new();
        public List<List<Guid>> StopRequests { get; } = [];
        public int ViolateStopFor { get; init; } = -1; // index of the StopActivityAsync call that reports a violation

        public void Add(Guid id, bool granted, List<string> resources, DateTime lastKeepAlive) =>
            Rows[id] = new()
            {
                ClientId = id, ResourceGroup = Group, ClientStatus = SqlServer.Clients.ClientStatus.Active,
                CoordinatorStatus = granted ? SqlServer.Clients.CoordinatorStatus.ResourcesGranted : SqlServer.Clients.CoordinatorStatus.StopActivity,
                AssignedResources = resources, LastKeepAlive = lastKeepAlive, FencingToken = 1,
            };

        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;

        public Task<List<SqlServer.Clients.Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(Rows.Values.Where(x => x.ClientStatus is SqlServer.Clients.ClientStatus.Waiting or SqlServer.Clients.ClientStatus.Active)
                .Select(x => Copy(x)).ToList());

        public Task<SqlServer.Clients.Client> KeepAliveAsync(Guid clientId)
        {
            Rows[clientId].LastKeepAlive = DateTime.UtcNow;
            return Task.FromResult(Copy(Rows[clientId]));
        }

        public Task SetClientStatusAsync(Guid clientId, SqlServer.Clients.ClientStatus clientStatus)
        {
            Rows[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<SqlServer.Clients.ModifyClientResult> StopActivityAsync(int fencingToken, List<SqlServer.Clients.Client> clients)
        {
            StopRequests.Add(clients.Select(x => x.ClientId).ToList());
            foreach (var c in clients.Select(x => Rows[x.ClientId]))
            {
                c.CoordinatorStatus = SqlServer.Clients.CoordinatorStatus.StopActivity;
                c.AssignedResources = [];
                c.FencingToken = fencingToken;
            }

            return Task.FromResult(StopRequests.Count - 1 == ViolateStopFor
                ? SqlServer.Clients.ModifyClientResult.FencingTokenViolation
                : SqlServer.Clients.ModifyClientResult.Ok);
        }

        public Task<SqlServer.Clients.ModifyClientResult> StartActivityAsync(int fencingToken, List<SqlServer.Clients.ClientStartRequest> requests)
        {
            foreach (var r in requests)
            {
                Rows[r.ClientId].CoordinatorStatus = SqlServer.Clients.CoordinatorStatus.ResourcesGranted;
                Rows[r.ClientId].AssignedResources = r.AssignedResources.ToList();
            }

            return Task.FromResult(SqlServer.Clients.ModifyClientResult.Ok);
        }

        private static SqlServer.Clients.Client Copy(SqlServer.Clients.Client x) =>
            new()
            {
                ClientId = x.ClientId, ResourceGroup = x.ResourceGroup, ClientStatus = x.ClientStatus,
                CoordinatorStatus = x.CoordinatorStatus, AssignedResources = x.AssignedResources.ToList(),
                LastKeepAlive = x.LastKeepAlive, TimeNow = DateTime.UtcNow, FencingToken = x.FencingToken,
            };
    }

    private sealed class RedisTable : Redis.Clients.IClientService
    {
        public Dictionary<Guid, Redis.Clients.Client> Rows { get; } = new();

        public void Add(Guid id, bool granted, List<string> resources, DateTime lastKeepAlive) =>
            Rows[id] = new()
            {
                ClientId = id, ResourceGroup = Group, ClientStatus = Redis.Clients.ClientStatus.Active,
                CoordinatorStatus = granted ? Redis.Clients.CoordinatorStatus.ResourcesGranted : Redis.Clients.CoordinatorStatus.StopActivity,
                AssignedResources = resources, LastKeepAlive = lastKeepAlive, FencingToken = 1,
            };

        public Task CreateClientAsync(string resourceGroup, Guid clientId) => Task.CompletedTask;

        public Task<List<Redis.Clients.Client>> GetActiveClientsAsync(string resourceGroup) =>
            Task.FromResult(Rows.Values.Where(x => x.ClientStatus is Redis.Clients.ClientStatus.Waiting or Redis.Clients.ClientStatus.Active)
                .Select(x => Copy(x)).ToList());

        public Task<Redis.Clients.Client> KeepAliveAsync(Guid clientId)
        {
            Rows[clientId].LastKeepAlive = DateTime.UtcNow;
            return Task.FromResult(Copy(Rows[clientId]));
        }

        public Task SetClientStatusAsync(Guid clientId, Redis.Clients.ClientStatus clientStatus)
        {
            Rows[clientId].ClientStatus = clientStatus;
            return Task.CompletedTask;
        }

        public Task<Redis.Clients.ModifyClientResult> StopActivityAsync(int fencingToken, List<Redis.Clients.Client> clients)
        {
            foreach (var c in clients.Select(x => Rows[x.ClientId]))
            {
                c.CoordinatorStatus = Redis.Clients.CoordinatorStatus.StopActivity;
                c.AssignedResources = [];
                c.FencingToken = fencingToken;
            }

            return Task.FromResult(Redis.Clients.ModifyClientResult.Ok);
        }

        public Task<Redis.Clients.ModifyClientResult> StartActivityAsync(int fencingToken, List<Redis.Clients.ClientStartRequest> requests)
        {
            foreach (var r in requests)
            {
                Rows[r.ClientId].CoordinatorStatus = Redis.Clients.CoordinatorStatus.ResourcesGranted;
                Rows[r.ClientId].AssignedResources = r.AssignedResources.ToList();
            }

            return Task.FromResult(Redis.Clients.ModifyClientResult.Ok);
        }

        private static Redis.Clients.Client Copy(Redis.Clients.Client x) =>
            new()
            {
                ClientId = x.ClientId, ResourceGroup = x.ResourceGroup, ClientStatus = x.ClientStatus,
                CoordinatorStatus = x.CoordinatorStatus, AssignedResources = x.AssignedResources.ToList(),
                LastKeepAlive = x.LastKeepAlive, TimeNow = DateTime.UtcNow, FencingToken = x.FencingToken,
            };
    }
}
