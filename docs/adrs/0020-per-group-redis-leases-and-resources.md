---
id: ADR-0020
status: Accepted
date: 2026-10-18
decides: "Redis key layout for leases and resource lists"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0015", "ADR-0017", "TM-0003", "US-0006" ]
supersedes: [ ]
superseded-by: ""
---

# Per-group Redis keys for leases and resource lists

## Context and Problem Statement

ADR-0015 moved Redis **client records** to one hash per group (3.0.0). Leases and resources are still shared by all
groups:

| Data      | Key (3.x)                       | Read pattern                                                          |
|-----------|---------------------------------|-----------------------------------------------------------------------|
| Leases    | `Dictionary:RBR:ResourceGroups` | one field per group, compare-and-set (ADR-0017)                       |
| Resources | `List:RBR:Resources`            | `LRANGE` of the **whole** list on every heartbeat, filtered in memory |

Two consequences, recorded as residual risks in TM-0003:

* **No isolation:** a Redis ACL user for group A can change group B's lease or resource list. Key patterns cannot
  separate groups that share a key.
* **Cost:** every node reads every group's resources on every heartbeat.

There is also no documented or tool-supported way to provision a Redis resource group. Operators write the hash field
and the list entries by hand, and `examples/Rebalancer.RabbitMq.Tools` supports SQL Server only.

This record decides the key layout for leases and resource lists.

## Decision Drivers

* Full per-group isolation with Redis ACL key patterns (completes ADR-0015).
* Heartbeat cost proportional to the group.
* No window in which two lease records for one group exist (mutual exclusion must hold across the change).
* Users get a warning before anything breaks, as with ADR-0011 and ADR-0013.

## Considered Options

* **A. Per-group keys within 3.x, dual layout.** Read the per-group keys and fall back to the shared ones, and move
  entries at the first coordinator tenure. No breaking release, but two places can hold a lease. Avoiding two
  coordinators needs a compare-and-set across both layouts on every acquire and renew, which makes the lease path more
  complex for as long as the fallback exists.
* **B. Keep the shared keys;** document separate Redis databases or instances per trust domain for isolation. No code
  change; the cost and isolation risks stay.
* **C. Deprecate in 3.x, switch in 4.0 with an automatic one-time migration.**
  * 3.x: document per-group provisioning, and log a warning when a group is still provisioned in the shared keys.
  * 4.0: leases in `RBR:Lease:{group}` and resources in `RBR:Resources:{group}` (group escaped as in ADR-0015).
  * When a 4.0 node starts and its group's per-group lease does not exist, it moves the group's lease field and
    resource entries out of the shared keys in one guarded transaction. The transaction fails if any of them changed
    meanwhile, so at most one node moves them.
  * Redis groups upgrade all nodes at once, as already required since 2.1.

## Decision Outcome

Proposed option: **C**.

The strongest argument against C is that it needs a major version and changes how operators provision groups. B
avoids both, and separate databases already give full isolation. But B leaves the whole-list resource read in place,
and TM-0003 shows that shared keys defeat the per-group ACL users that 3.0 just made possible. A avoids the major
version, but keeps two lease layouts alive, which risks mutual exclusion, the one guarantee the library exists for.

C keeps exactly one lease record per group at any time: the move is a single guarded transaction, and the
all-nodes-at-once rule rules out mixed versions. It also follows the deprecate-then-remove pattern of ADR-0011 and
ADR-0013.

### Consequences

* Good, because a Redis ACL user can be confined to one group completely, and heartbeat reads scale with the group.
* Good, because operators get a documented provisioning procedure (needed in any case).
* Bad, because 4.0 is breaking for provisioning scripts and needs a migration guide and change record. Rollback to 3.x
  needs a reverse move, or re-provisioning in the shared keys.
* Bad, because two ADRs (0015 and this one) together define the Redis layout; a future reader must read both.

### Confirmation

* 3.x: a test that a group provisioned only in the shared keys logs the deprecation warning once per session.
* 4.0: integration tests (Docker Redis) that a 3.x-provisioned group is migrated on first start, and that two nodes
  starting together move it only once. A minimal per-group ACL user (TM-0003) must work end to end with **no** access
  to the shared keys.

## More Information

* Completes [ADR-0015](0015-store-redis-clients-per-resource-group.md); lease semantics stay as in
  [ADR-0017](0017-redis-lease-timing-and-atomic-updates.md) and [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md).
* Follow-up of [TM-0003](../threat-models/TM-0003-redis-coordination.md) (residual risks).
* Releases: deprecation in a 3.x minor; layout change in 4.0.0.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (ADR-0002: Coordinator, follower,
  lease, and resource store pattern for SQL Server and Redis backends)
- `docs/adrs/0015-store-redis-clients-per-resource-group.md` (ADR-0015: Store Redis client records in one hash per
  resource group)
- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (ADR-0017: Redis leases use the server clock and
  compare-and-set updates)
- `docs/threat-models/TM-0003-redis-coordination.md` (TM-0003: Redis coordination backend)
- `docs/user-stories/US-0006-coordinate-via-redis-backend.md` (US-0006: Coordinate via Redis backend)
