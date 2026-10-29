---
id: ADR-0021
status: Accepted
date: 2026-10-19
decides: "whether and how resource handlers receive a token that lets downstream systems reject writes from a stale assignment"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0019", "ADR-0002", "ADR-0003", "PRB-0003", "TM-0002", "TM-0003", "US-0002", "US-0005", "US-0006", "US-0007" ]
supersedes: [ ]
superseded-by: ""
---

# No fencing token for resource handlers for now (assignment generation deferred)

## Context and Problem Statement

ADR-0019 (option A, released in 3.1.0) shortens, but does not close, the PRB-0003 window. A process paused past the
keep-alive expiry resumes all of its threads at once, so its resource handlers can run until its first follower tick
while another node already holds the same resource. No library-side change can prevent that, because the library
does not run between "process resumes" and "handler writes". Only the **downstream system** can, if it rejects writes
that carry an older token than one it has already seen for that resource (fencing, as in Kleppmann, "How to do
distributed locking", 2016). ADR-0019 left this as option C, for a separate decision.

This record decides whether the library exposes such a token to resource handlers, and what the token is.

**What a token must guarantee.** For any resource, a handler that is given the resource later must get a strictly
greater token than every handler that held it before. A token that is only unique is not enough.

**Finding while drafting (verified in code, 2026-10-19).** On SQL Server and Redis the existing fencing token is per **coordinator lease**: it increases when a new coordinator acquires the lease, and stays the same across every
rebalance within one tenure (`Roles/Coordinator.cs` passes `clientEvent.FencingToken` to `StartActivityAsync` and
`StopActivityAsync`; `ClientService.StartActivityAsync` stores it). In the PRB-0003 scenario the **same** coordinator
reassigns the resource, so the stale follower and the new holder carry the same token. Exposing that token unchanged,
as ADR-0019 option C reads, would not fence the case it is meant to fix. ZooKeeper behaves the same way: its epoch is
the version of the group's `epoch` znode, which a coordinator increments once, when it takes over (`BecomeCoordinatorAsync` → `IncrementAndWatchEpochAsync` in `ResourceBarrier/Coordinator.cs` and
`GlobalBarrier/Coordinator.cs`); rebalances within one tenure keep it. (Corrected 2026-10-19, see Erratum.)

## Decision Drivers

* Correctness: the token must increase on every reassignment, including within one coordinator's tenure.
* Mutual exclusion stays the library's core guarantee (ADR-0002, ADR-0003); fencing complements it and must not
  weaken the existing protocol.
* Additive public API only; consumers that ignore the token keep working unchanged.
* One meaning across the three backends.
* Cost of the data change (SQL Server schema, Redis record format) and of the upgrade path.

## Considered Options

* **A. Expose the lease fencing token as-is** (SQL Server, Redis) and the epoch (ZooKeeper) on `OnAssignmentArgs`.
  All three change per coordinator tenure, not per rebalance.
* **B. A persisted assignment generation per group**, incremented by the coordinator on every rebalance under the
  fencing-token guard, written with each client's assignment, and exposed on `OnAssignmentArgs`.
* **C. A derived generation** `(lease fencing token << 32) | per-tenure rebalance counter`, held in the coordinator's
  memory and written with each client's assignment; no new group-level state.
* **D. No token:** keep ADR-0019 option A and the operational advice (keep-alive expiry well above the longest pause).

## Decision Outcome

Chosen option: **D, no token**. The maintainer accepted this record on 2026-10-19 and asked for the smallest complete
option where a choice remained ("ponytail" rule: skip what nothing in the repository needs yet).

The strongest argument against D is that it leaves the PRB-0003 window open: a process paused longer than the
keep-alive expiry can run its handlers for up to one follower interval while another node holds the same resource,
and a consumer has no way to prevent that. Only B closes it.

D is chosen anyway, because nothing in the repository shows a consumer that needs, or could use, this guarantee:
B changes the SQL Server schema and the Redis record format and adds public API, and it helps only consumers whose
downstream system can store and compare a token per resource (RabbitMQ consumers, the repository's own example,
cannot). ADR-0019's fix and the operational advice (keep the keep-alive expiry well above the longest expected pause)
already reduce the risk for every consumer, at no cost.

A is rejected for good: it is wrong for all three backends (see the finding above) and would advertise a guarantee
the token does not give. C is rejected because its counter can go backwards; fixing that is B.

**When to revisit:** a consumer reports duplicate processing after a long pause, or asks for a token. B is then the
starting point, as sketched here:

* `OnAssignmentArgs` gains `long AssignmentGeneration` (additive; the existing constructor stays and passes 0).
  `AssignedResources` gains the same property.
* SQL Server: `RBR.ResourceGroups.AssignmentGeneration BIGINT NOT NULL DEFAULT 0` and
  `RBR.Clients.AssignmentGeneration BIGINT NOT NULL DEFAULT 0`. The coordinator increments the group's value in the
  same guarded statement that checks its fencing token, then writes it with each `StartActivity`.
* Redis: an `AssignmentGeneration` field in the group's lease record and in each client record, incremented with the
  same compare-and-set as the lease (ADR-0017). Missing fields read as 0.
* ZooKeeper: a persisted generation as well (for example a counter znode the coordinator increments on every
  rebalance); the epoch changes per coordinator tenure only, so it is not enough. (Corrected 2026-10-19, see Erratum.)

### Consequences

* Good, because no schema, record format or public API changes, and nothing new to keep correct across coordinator
  changes, recovery and the ADR-0014 record expiry.
* Bad, because the window that ADR-0019 shortened stays open: up to one follower interval of duplicate processing
  after a pause longer than the keep-alive expiry. Release notes (3.1.0) and PRB-0003 already say so.
* Bad, because consumers with a fenceable downstream system cannot close the window through the library; they can
  only fence on their own data (for example, an owner column they update).

### Confirmation

* PRB-0003's corrective action "fencing token" points here, with this outcome.
* The operational advice stays in the 3.1.0 release notes and PRB-0003 ("Known errors / workarounds").
* The `*PausedNodeTests` integration tests keep confirming the window ADR-0019 guarantees: a resumed follower stops at
  its first tick.

## Pros and Cons of the Options

### A. Expose the lease fencing token as-is

* Good, because no data change: the value is already stored with each client record.
* Bad, because it does not increase on a reassignment within one coordinator tenure, which is the PRB-0003 case.

### B. Persisted assignment generation

* Good, because it is strictly increasing per group on every rebalance, and survives coordinator changes and restarts.
* Bad, because it needs a schema change (SQL Server), a record format change (Redis) and a migration path.

### C. Derived generation

* Good, because it adds no group-level state.
* Bad, because a coordinator role restart under the same lease resets the counter, so the generation can go backwards.
* Bad, because it still needs the per-client value stored, so the record changes anyway.

### D. No token

* Good, because there is no cost, and ADR-0019 plus the operational advice already cover most deployments.
* Bad, because a pause longer than the keep-alive expiry can still cause up to one follower interval of duplicate
  processing, with no way for a consumer to prevent it.

## More Information

* Follow-up of [ADR-0019](0019-stop-unresponsive-followers-holding-reassigned-resources.md) (option C) and
  [PRB-0003](../problem-records/PRB-0003-resumed-follower-keeps-reassigned-resources.md) (corrective action
  "fencing token for resource handlers").
* Protocol: [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) (SQL Server, Redis),
  [ADR-0003](0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md) (ZooKeeper).
* Threat models: [TM-0002](../threat-models/TM-0002-sql-server-coordination.md),
  [TM-0003](../threat-models/TM-0003-redis-coordination.md).
* Handler API: [US-0002](../user-stories/US-0002-react-to-assignment-unassignment-and-abort.md); backends:
  [US-0005](../user-stories/US-0005-coordinate-via-sql-server-backend.md),
  [US-0006](../user-stories/US-0006-coordinate-via-redis-backend.md),
  [US-0007](../user-stories/US-0007-coordinate-via-zookeeper-with-barrier-mode.md).
* External: M. Kleppmann, "How to do distributed locking" (2016), fencing tokens.
* Outcome D: no implementation, story or change record follows from this record.

## Erratum (2026-10-19)

The accepted text said that ZooKeeper's coordinator increments the `epoch` znode on every rebalance. The code shows it
increments it once per coordinator tenure (`BecomeCoordinatorAsync`), like the SQL Server and Redis fencing token;
verified while implementing ADR-0022. Corrected in place in Context ("Finding"), option A, the rejection of A, and the
option-B sketch for ZooKeeper. The decision (option D) is unchanged; the correction only strengthens the case against
A. Corrected in place rather than superseded, at dradoaica's explicit request (2026-10-19), as an exception to the
immutability of accepted records.

## Sources

- `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md` (ADR-0019: Stop unresponsive followers
  that still hold reassigned resources)
- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (ADR-0002: Coordinator, follower,
  lease, and resource store pattern for SQL Server and Redis backends)
- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md` (ADR-0003: ZooKeeper backend: leader
  election, barrier algorithms, and rebalancing modes)
- `docs/problem-records/PRB-0003-resumed-follower-keeps-reassigned-resources.md` (PRB-0003: A resumed SQL Server / Redis
  follower keeps processing resources that were reassigned)
- `docs/threat-models/TM-0002-sql-server-coordination.md` (TM-0002: SQL Server coordination backend)
- `docs/threat-models/TM-0003-redis-coordination.md` (TM-0003: Redis coordination backend)
- `docs/user-stories/US-0002-react-to-assignment-unassignment-and-abort.md` (US-0002: React to assignment, unassignment,
  and fatal abort)
- `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md` (US-0005: Coordinate via SQL Server backend)
- `docs/user-stories/US-0006-coordinate-via-redis-backend.md` (US-0006: Coordinate via Redis backend)
- `docs/user-stories/US-0007-coordinate-via-zookeeper-with-barrier-mode.md` (US-0007: Coordinate via ZooKeeper with
  selectable barrier mode)
