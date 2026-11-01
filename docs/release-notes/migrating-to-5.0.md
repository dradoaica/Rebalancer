# Migrating from 4.x to 5.0 (SQL Server and Redis)

Only code that uses the SQL Server or Redis service types is affected. ZooKeeper has no 5.0 migration steps.

Decision: [ADR-0029](../adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md).

## Requires .NET 10

[ADR-0033](../adrs/0033-target-net-10-only.md). Every 5.0 package targets `net10.0` (4.x: `netstandard2.1`), so a
host must run on .NET 10 or later. .NET 8 and .NET 9 reach end of support on November 10, 2026; a host that stays on
them stays on 4.3.x.

## What changes

The coordination code that SQL Server and Redis had as two copies (leader election under a lease, the coordinator and
follower roles, the assignment store) moves, unchanged, into one new package, `Rebalancer.LeaseCoordination`.
`Rebalancer.SqlServer` and `Rebalancer.Redis` depend on it, so NuGet installs it with them; nothing to add by hand.

Stored data does not change: the SQL Server `RBR` schema and the Redis `RBR:*` keys are the same as in 4.3.

## Who has to change code

A host that only creates a provider from a connection string, or registers one with
`Rebalancer.Extensions.DependencyInjection`, needs no code change; recompile against 5.0.

Code that names the types below (for example a custom `ILeaseService` / `IClientService` / `IResourceService` passed to
the `SqlServerProvider` or `RedisProvider` constructor, or test doubles of them) must change its `using` directives.
Each type exists once now instead of once per backend:

| 4.x namespaces                                                 | 5.0 namespace                            | Types                                                                                                                          |
|----------------------------------------------------------------|------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------|
| `Rebalancer.SqlServer.Clients`, `Rebalancer.Redis.Clients`     | `Rebalancer.LeaseCoordination.Clients`   | `Client`, `ClientStartRequest`, `ClientStatus`, `CoordinatorStatus`, `IClientService`, `ModifyClientResult`                    |
| `Rebalancer.SqlServer.Leases`, `Rebalancer.Redis.Leases`       | `Rebalancer.LeaseCoordination.Leases`    | `AcquireLeaseRequest`, `ILeaseService`, `Lease`, `LeaseResponse`, `LeaseResult`, `RelinquishLeaseRequest`, `RenewLeaseRequest` |
| `Rebalancer.SqlServer.Resources`, `Rebalancer.Redis.Resources` | `Rebalancer.LeaseCoordination.Resources` | `IResourceService`                                                                                                             |
| `Rebalancer.SqlServer.Roles`, `Rebalancer.Redis.Roles`         | `Rebalancer.LeaseCoordination.Roles`     | `ClientEvent`, `CoordinatorToken`, `EventType`                                                                                 |
| `Rebalancer.SqlServer.Store`, `Rebalancer.Redis.Store`         | `Rebalancer.LeaseCoordination.Store`     | `AssignmentStatus`                                                                                                             |

Unchanged: `SqlServerProvider`, `RedisProvider` and their constructors (only the namespaces of the service parameter
types above), `Rebalancer.Redis.ResourceGroup`, `Rebalancer.Redis.Resources.Resource`,
`Rebalancer.SqlServer.Connections.ConnectionHelper`, and everything in `Rebalancer.Core`.

The change also breaks binary compatibility: a library compiled against 4.x that uses these types must be recompiled
against 5.0.

## Upgrading nodes

The coordination protocol and the stored data are unchanged, so 5.0 needs no data migration. Whether a group can run
4.3 and 5.0 nodes together during a rolling upgrade is confirmed by the rollback rehearsal of the 5.0.0 change record;
until then, upgrade all nodes of a group at once, as for 4.0.

## SQL Server: optional schema step

[ADR-0032](../adrs/0032-no-foreign-key-from-client-records-to-resource-groups.md). The shipped
`db/sqlserver/RebalanserTables.sql` no longer creates the foreign key `FK_Clients_ResourceGroups`. With it, a client
whose resource group does not exist fails inside `StartAsync`; without it, the client runs and reports
`ClientState.Aborted` (without auto-recovery), as documented since 4.2.0. A database created from the 4.x scripts keeps
the key until you drop it; 5.0 works with or without it:

```sql
ALTER TABLE RBR.Clients DROP CONSTRAINT FK_Clients_ResourceGroups;
```

Rolling back to 4.x needs no step: 4.x works without the key too. A client started for a missing group leaves its
record in `RBR.Clients` until a clean stop, or until the group is created and its coordinator expires the record.
