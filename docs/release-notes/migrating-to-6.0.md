# Migrating from 5.x to 6.0

6.0 has two breaking changes. Most hosts need only the first step for Redis; SQL Server, PostgreSQL and ZooKeeper
hosts need no change unless their code uses one of the types listed below.

Decisions: [ADR-0034](../adrs/0034-public-api-of-the-lease-based-backends-in-6-0.md),
[ADR-0037](../adrs/0037-redis-guarded-writes-as-server-side-scripts.md). Threat model:
[TM-0003](../threat-models/TM-0003-redis-coordination.md) (revision 7).

## Redis and Valkey: allow scripts for the nodes' ACL user, before upgrading

In 6.0 the Redis backend makes its conditional writes (keep-alives, leases, assignments, record expiry) as server-side
Lua scripts instead of `WATCH` transactions, so that writes by different nodes no longer make each other fail on slow
links ([PRB-0009](../problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md)). Nodes therefore run
`EVAL` and `EVALSHA`.

- **A node connecting as `default` or as a user with all commands:** nothing to do.
- **A per-group ACL user** (the documented least-privilege setup): add `+eval +evalsha` **before** the first node of
  the group runs 6.0, for example

  ```
  ACL SETUSER orders-user +eval +evalsha
  ```

  The full 6.0 user for one group:

  ```
  ACL SETUSER orders-user on >secret resetchannels ~RBR:Lease:orders ~RBR:Resources:orders ~RBR:Clients:orders +@read +@write +@transaction +time +echo +eval +evalsha
  ```

  Scripts run under the user's ACL: a script cannot reach keys the user may not access (verified on Redis 7.0 and
  Valkey 8.1). The two commands let anyone with the user's password run arbitrary Lua over the group's keys, including
  a long script that blocks the whole Redis instance while it runs (TM-0003, D5); keep the credentials as secret as
  before, and prefer a Redis instance dedicated to Rebalancer.
- **A Redis service that does not allow scripts:** stay on 5.2 until it does.

The stored keys, layout and JSON format do not change, and 5.2 and 6.0 nodes can run in one group, so a group can be
upgraded one node at a time once the ACL allows the two commands. A 5.2 node needs nothing new.

## Types that are no longer public

These types were deprecated in 5.2 (warning **RBR0034**) and are internal in 6.0. No public signature used them; the
providers, `RebalancerClient` and the service interfaces are unchanged.

| Type                                                       | Package                        |
|------------------------------------------------------------|--------------------------------|
| `Rebalancer.LeaseCoordination.Roles.ClientEvent`           | `Rebalancer.LeaseCoordination` |
| `Rebalancer.LeaseCoordination.Roles.CoordinatorToken`      | `Rebalancer.LeaseCoordination` |
| `Rebalancer.LeaseCoordination.Roles.EventType`             | `Rebalancer.LeaseCoordination` |
| `Rebalancer.LeaseCoordination.Store.AssignmentStatus`      | `Rebalancer.LeaseCoordination` |
| `Rebalancer.Redis.ResourceGroup`                           | `Rebalancer.Redis`             |
| `Rebalancer.Redis.Resources.Resource`                      | `Rebalancer.Redis`             |
| `Rebalancer.Redis.Utils.Constants`                         | `Rebalancer.Redis`             |
| `Rebalancer.SqlServer.Connections.ConnectionHelper`        | `Rebalancer.SqlServer`         |

A host that compiles against 5.2 without warning RBR0034 compiles against 6.0 unchanged. Code that used one of these
types keeps its own copy (they are small data classes, an enum, a constant and a connection helper).

## Rolling back

Redeploy 5.2: no data to undo. The ACL commands added above can stay; 5.2 does not use them.
