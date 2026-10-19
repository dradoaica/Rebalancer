---
id: ADR-0015
status: Accepted
date: 2026-10-18
decides: "Redis key layout for client records"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0008", "ADR-0014", "TM-0003", "US-006" ]
supersedes: [ ]
superseded-by: ""
---

# Store Redis client records in one hash per resource group

## Context and Problem Statement

All client records of all resource groups live in one hash, `Dictionary:RBR:Clients` (field: client id, value: JSON).
`GetActiveClientsAsync` reads the whole hash (`HVALS`) on every heartbeat of every node and filters by group in memory.
Cost therefore grows with the number of groups and with every client ever registered. Groups also cannot be isolated:
one Redis ACL key pattern covers every group. TM-0003 lists both as residual risks. The key name was kept from
`StackExchange.Redis.DataTypes` for compatibility; ADR-0008 removed that dependency, so the layout is now free to
change. This record decides the layout of client records in 3.0.

## Decision Drivers

* Heartbeat cost proportional to the group, not the installation.
* Per-group isolation possible with Redis ACL key patterns.
* A safe migration from 2.x data, compatible with the Redis all-nodes-at-once upgrade rule (CR-0001).

## Considered Options

* **A. One hash per group:** `RBR:Clients:{group}`. The provider reads and writes only its group's hash. On startup, a
  3.0 node moves its group's entries from the 2.x hash `Dictionary:RBR:Clients` into the new hash, once, inside a
  transaction guarded by the group's lease.
* **B. Keep the shared hash, add a per-group index set** of client ids. Reads become `HMGET` of the indexed ids; two
  structures to keep consistent; isolation still impossible.
* **C. Keep the shared hash** and rely on ADR-0014 to bound its size. Fixes growth, not the per-group cost or isolation.

## Decision Outcome

Proposed option: **A**. The strongest argument against A is that it is a data-format change: a 2.x node and a 3.0 node
in one group would look at different hashes and could both coordinate. That is already ruled out for Redis, where
mixed versions are unsupported and every node of a group is upgraded at once (CR-0001). The 3.0 change record
repeats the rule. Rollback to 2.x needs the reverse move. With ADR-0014 in place, a fresh start with an empty hash is
also acceptable, because nodes re-register. Group names are used as key segments, so they must not contain characters
that clash with key patterns. The key segment is the group name, escaped (rule fixed at implementation).

### Consequences

* Good, because heartbeat reads scale with the group, and a Redis ACL user can be limited to one group's keys
  (resolves a TM-0003 residual risk).
* Bad, because 3.0 Redis upgrades need the migration step and a matching rollback procedure.
* Bad, because the leases (`Dictionary:RBR:ResourceGroups`) and resources (`List:RBR:Resources`) stay shared. Moving
  them is out of scope here; the resources list has the same whole-list read pattern (follow-up).

### Confirmation

* Integration test (Docker Redis): data written in the 2.x layout (seeded the way `RedisAssignmentSafetyTests` seeds
  today) is migrated, assignment works, and the 2.x hash no longer holds the group's entries.
* Integration test: two groups in one Redis instance never read each other's client records.

## More Information

* Follow-up of [TM-0003](../threat-models/TM-0003-redis-coordination.md) (residual risks). Builds on
  [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md),
  [ADR-0008](0008-replace-redis-datatypes-with-direct-newtonsoft-json.md) and
  [ADR-0014](0014-expire-stale-client-records.md).
* Release: 3.0.0 (breaking data-format change for Redis).
