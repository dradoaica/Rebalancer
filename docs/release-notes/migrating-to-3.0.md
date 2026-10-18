# Migrating from 2.x to 3.0

Upgrade to 2.2 first and fix its compiler warnings (CS0618): every API that 3.0 removes was marked `[Obsolete]` in 2.2,
with the replacement in the warning message. Then follow the sections that apply to you.

## Everyone: pass the provider explicitly (ADR-0013)

`Providers.Register(...)` and the parameterless `new RebalancerClient()` are gone.

```csharp
// 2.x
Providers.Register(() => new SqlServerProvider(connectionString));
using var client = new RebalancerClient();

// 3.0
using var client = new RebalancerClient(new SqlServerProvider(connectionString));
```

With dependency injection, nothing changes: `AddRebalancerSqlServer`, `AddRebalancerRedis`, `AddRebalancerZooKeeper`
and `AddRebalancerProvider` register a new provider per client.

## ZooKeeper: credentials or an explicit ACL (ADR-0011, ADR-0012)

Removed: `new ZooKeeperService(hosts)`, the `ZooKeeperProvider` constructor ending in `IZooKeeperService zooKeeperService
= null`, and `AddRebalancerZooKeeper(..., IZooKeeperService? zooKeeperService = null)`. The remaining entry points throw
`ArgumentException` if you give neither credentials nor an ACL.

| You used                                       | Use in 3.0                                                                                           |
|------------------------------------------------|------------------------------------------------------------------------------------------------------|
| No credentials (open znodes)                   | the ACL overload with `acl: ZooDefs.Ids.OPEN_ACL_UNSAFE, authScheme: null, authData: null` – same behavior, now explicit |
| Credentials (`authScheme`, `authData`)         | unchanged                                                                                            |
| A custom `IZooKeeperService`                   | `new ZooKeeperProvider(zooKeeperService, rootPath, sessionTimeout, connectTimeout, minimumRebalancingInterval, mode, logger)`; with DI, `AddRebalancerProvider(_ => new ZooKeeperProvider(new MyService(), ...))` |

See `src/Rebalancer.ZooKeeper/README.md` for securing znodes, including the `resources` znode you create yourself.

## SQL Server: grant `DELETE` on `RBR.Clients` (ADR-0014)

3.0 deletes client records that have not sent a keep-alive for a long time, and a node deletes its own record when it
stops cleanly. The login used by Rebalancer needs:

```sql
GRANT DELETE ON [RBR].[Clients] TO [your_rebalancer_login];
```

No schema change. Without the grant, coordination keeps working, but stale records are not deleted: the coordinator
logs the warning "Could not expire stale client records" on every heartbeat.

## Redis: upgrade all nodes of a resource group at once (ADR-0015)

Client records move from the shared hash `Dictionary:RBR:Clients` to one hash per group, `RBR:Clients:{group}` (group
name percent-encoded outside `A-Z a-z 0-9 - _ . ~`). As with the 2.1 upgrade, a group must not run 2.x and 3.0 nodes
together: they would look at different hashes.

1. Stop every node of the group.
2. Deploy 3.0 and start the nodes. The first coordinator moves the group's old entries into the new hash, guarded by
   its lease. Those entries belong to the stopped processes and expire later (ADR-0014).
3. Optional: a Redis ACL user can now be limited to one group's client hash, e.g.
   `~RBR:Clients:orders ~Dictionary:RBR:ResourceGroups ~List:RBR:Resources` (leases and resources stay shared).

**Rollback to 2.x:** stop the group's nodes and start 2.x. 2.x ignores `RBR:Clients:*`; its nodes register new records
in the shared hash at start, so nothing needs to move back. You may delete the group's `RBR:Clients:{group}` key
afterwards.

## Client record retention (ADR-0014)

New option `ClientOptions.ClientRecordRetention`: how long a record may go without a keep-alive before the coordinator
deletes it. Default: 10 × the resource group's keep-alive expiry period (`LeaseExpirySeconds`). A node whose record was
deleted (for example after a very long pause) stops its local activity (`OnUnassignment`) and registers again.

## Custom `IClientService` implementations (SQL Server, Redis)

Your implementation keeps compiling: the two new members have default implementations.

- `DeleteExpiredClientsAsync(...)` defaults to deleting nothing (2.x behavior). Implement it to get the cleanup.
- `DeleteClientAsync(clientId)` defaults to marking the client `Terminated` (2.x behavior on a clean stop).
- Automatic re-registration only works with the built-in services: they signal a missing record in a way the roles
  recognize. A custom implementation that throws its own exception keeps the 2.x behavior (the role faults).
