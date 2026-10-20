---
id: ADR-0014
status: Accepted
date: 2026-10-18
decides: "lifetime of client records in the SQL Server and Redis stores"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "TM-0002", "TM-0003", "US-0005", "US-0006" ]
supersedes: [ ]
superseded-by: [ ]
---

# Expire stale client records in the SQL Server and Redis stores

## Context and Problem Statement

Neither backend ever deletes a client record (no `DELETE` / `HDEL` exists in `src/`). Every process start registers a
new client id. Records that are no longer used stay forever, in two states:

* **`Terminated`:** set when a node's main loop stops. It is *not* final: a node that recovers reuses its record and
  moves it back to `Waiting` (`Roles/Coordinator.cs`).
* **`Waiting` / `Active` with a stale keep-alive:** a node that crashed or was killed never updates its record again.

The tables and hashes therefore grow with every restart. TM-0002 rates this Low to Medium for SQL Server. TM-0003
rates it **Medium** for Redis, because every node reads the whole clients hash on every heartbeat. This record decides
when a client record may be removed, and by whom.

## Decision Drivers

* Bounded storage and heartbeat cost, without operator jobs.
* Never remove the record of a node that is still alive or recovering.
* Keep fencing safe: removal must not let a stale coordinator act on a record it no longer owns.

## Considered Options

* **A. Expire by keep-alive age, done by the coordinator.**
  * During each heartbeat, the current coordinator deletes records of its resource group whose `LastKeepAlive` is older
    than a retention period. The default is 10 × the keep-alive expiry period; it is configurable.
  * The delete is guarded by the coordinator's fencing token, like the other coordinator writes.
  * A node that comes back after its record expired re-registers automatically. That is new behavior: today a provider
    calls `CreateClientAsync` only at start or when the host calls `RecreateClientAsync`, and a missing record makes
    the Redis keep-alive fail with "No client exists".
* **B. Delete on clean shutdown only:** the provider deletes its own record when stopped. Crashed nodes still leak.
* **C. Operator job:** document a cleanup script (TM-0002 / TM-0003 follow-ups); no library change.
* **D. Redis expiry:** `EXPIRE` works on whole keys only. Per-field expiry (`HEXPIRE`) exists only in newer Redis
  versions (7.4 or later; unverified – please confirm), and the test image is `redis:7.0`. It also leaves SQL Server
  unsolved.

## Decision Outcome

Proposed option: **A**, plus B as an optimization (delete own record on clean `StopAsync`). The strongest argument
against A is that a coordinator now deletes other nodes' data. A wrong retention period or clock problem could remove a
live node's record. The retention is a multiple of the keep-alive expiry already used to declare a node dead, so a node
whose record expires was dead for 10 detection periods. A node that returns anyway re-registers instead of failing.
Both backends judge age by the store's clock (SQL Server `GETUTCDATE()`, Redis `TIME`), not the coordinator's.

### Consequences

* Good, because storage and Redis heartbeat cost stay proportional to live nodes.
* Bad, because the SQL Server login needs `DELETE` on `RBR.Clients`. That relaxes the least-privilege recommendation
  in TM-0002, which must be updated: `DELETE` on `Clients` only.
* Bad, because a node returning after a very long pause must re-register; the provider must treat "my record is gone"
  as a recoverable condition, not a fatal error.

### Confirmation

* Unit tests (both backends, with the in-memory client fakes used by `CoordinatorDemotionTests`): a record older than
  the retention period is removed, a younger one is kept, a `Terminated` record with a recent keep-alive is kept.
* Integration test per backend: a killed node's record disappears after the retention period, and a restarted node
  re-registers.

## More Information

* Follow-ups of [TM-0002](../threat-models/TM-0002-sql-server-coordination.md) (Denial of Service D1) and
  [TM-0003](../threat-models/TM-0003-redis-coordination.md) (Denial of Service D1). Builds on
  [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md).
* Release: 3.0.0 (behavior change; new configuration option).
