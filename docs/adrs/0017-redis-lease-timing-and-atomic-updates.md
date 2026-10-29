---
id: ADR-0017
status: Accepted
date: 2026-10-18
decides: "clock source and update atomicity for Redis leases and client records"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0006", "US-0006" ]
supersedes: [ "ADR-0006" ]
superseded-by: ""
---

# Redis leases use the server clock and compare-and-set updates

## Context and Problem Statement

This record carries over ADR-0006 item 3 **unchanged**, as part of splitting ADR-0006 into one-concern records (with ADR-0016 and ADR-0018). It introduces no new decision.

Leases must not depend on client clock skew, and two coordinators must not both win a lease (ADR-0002 fencing).

## Decision Drivers

* Lease expiry judged on one clock.
* No lost updates between concurrent writers of a lease or of client records.

## Considered Options

* **A. Redis server time (`TIME`) and optimistic, condition-guarded transactions** (as decided in ADR-0006, item 3).
* **B. Client clocks and unconditional writes** (the 2.0.x behavior, replaced by ADR-0006).

## Decision Outcome

Proposed option: **A**, as already in force since 2.1.0: Redis lease and keep-alive times use Redis server time (`TIME`), and lease and client updates are compare-and-set transactions. The stored data format did not change (`Dictionary:` / `List:` keys, Newtonsoft JSON). The strongest argument against A: a group that mixes versions would
judge leases with two clocks. That is why Redis groups are upgraded all nodes at once (CR-0001).

### Consequences

* Good, because lease expiry is evaluated on one clock and lease writes are compare-and-set.
* Bad, because Redis groups cannot run mixed 2.0.x and later versions.

### Confirmation

* `tests/Rebalancer.UnitTests/RedisLeaseDecisionTests.cs`, `tests/Rebalancer.IntegrationTests/Redis/` (existing).

## More Information

* Replaces item 3 of [ADR-0006](0006-provider-resource-ownership-and-zookeeper-acl.md); see
  [ADR-0016](0016-rebalancer-client-owns-its-provider.md) for how ADR-0006 is marked superseded.
  [ADR-0015](0015-store-redis-clients-per-resource-group.md) changes the key layout of client records in 3.0, not
  this record's clock and atomicity rules.
* No code change.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (ADR-0002: Coordinator, follower,
  lease, and resource store pattern for SQL Server and Redis backends)
- `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md` (ADR-0006: Provider resource ownership, Redis
  server-time leases, and ZooKeeper ACLs)
- `docs/user-stories/US-0006-coordinate-via-redis-backend.md` (US-0006: Coordinate via Redis backend)
