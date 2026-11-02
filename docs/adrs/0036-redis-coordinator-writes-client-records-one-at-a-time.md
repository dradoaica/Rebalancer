---
id: ADR-0036
status: Accepted
date: 2026-11-02
decides: "how the Redis coordinator writes several client records in one grant or stop (PRB-0009)"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only (dradoaica, in session 2026-11-02)
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0017", "ADR-0020", "ADR-0026", "ADR-0029", "TM-0003", "PRB-0009" ]
supersedes: [ ]
superseded-by: ""
---

# The Redis coordinator writes client records one at a time

## Context and Problem Statement

The Redis coordinator grants and stops activity with one compare-and-set over every target client record and the lease
(`ModifyClientsAsync` in `src/Rebalancer.Redis/Clients/ClientService.cs`; ADR-0017's compare-and-set, ADR-0026's lease
guard): one `MULTI/EXEC`, conditioned on each record and the lease being unchanged since it read them, retried up to
10 times. Every node's keep-alive rewrites its own record once per heartbeat, and the coordinator's renewal loop
rewrites the lease several times per heartbeat. When a write attempt lasts about as long as a heartbeat (a slow link),
nearly every attempt meets one of those writes; after 10 failures the coordinator aborts and rebalances again, and the
group may not settle for minutes (PRB-0009: 75 failed attempts in one replay, each 640–2143 ms, every one with a changed
client record). SQL Server and PostgreSQL lock the rows and are not affected.

This record decides how the Redis coordinator writes several client records. Safety must not change: no write
without the lease (ADR-0026), and no node is granted a resource another node may still process.

## Considered Options

* **A. One compare-and-set per record:** each target record is written in its own `MULTI/EXEC`, conditioned on that
  record and the lease; the attempts per record restart for each record. A grant or stop may then be applied to some
  records and not others.
* **B. A server-side script** (Lua `EVAL`/`EVALSHA`) that checks each record's fencing token and the lease and writes
  them all, in one round trip and without conditions on whole values. Needs scripting commands in the per-group ACL
  user (`+@scripting`; TM-0003's minimal command set) and changes ADR-0017's compare-and-set rule.
* **C. Keep-alive times in their own hash** (`RBR:KeepAlive:{group}`), so keep-alives no longer change the records
  the coordinator compares. Changes the stored format (ADR-0020); 5.1 and later nodes could not share a group.
* **D. More attempts, or back-off between them:** no format, ACL or model change, but each attempt's chance of
  meeting a keep-alive stays the same.

## Decision Outcome

Chosen option: "A. One compare-and-set per record", because it removes the conflict that dominates PRB-0009 (an
attempt now meets at most one node's keep-alive, not all of them) while keeping ADR-0017's compare-and-set, the stored
format, and the ACL command set. Safety rests on three properties that already hold:

1. **Each write is still guarded.** Every per-record transaction carries ADR-0026's lease condition; a record is
   written only while the lease names this coordinator and token.
2. **A partial stop grants nothing.** The coordinator grants only after every node confirmed its stop
   (`Coordinator`'s stop barrier); a stop applied to some records makes it fail or step down before any grant.
   `RebalancerStore.tla` already allows a stop to reach any subset of records (`StoppedRecords`).
3. **A partial grant never overlaps.** Grants of one rebalance assign disjoint resources, so a grant applied to some
   records starts no resource twice; the remaining resources are unassigned until the next rebalance, which stops
   every node first.

The strongest argument against A: the grant loses its all-or-nothing property, which `RebalancerStore.tla` models
(`GrantedRecords`, "all records or none") and the safety proof rests on. A partial grant is a state the model has not
checked. Given that, A is accepted only with the model changed to grant any subset and rechecked (Confirmation).

Implementation outline (to be confirmed in review):

* `ModifyClientsAsync` loops over the target records; for each, the existing read, token check, lease read and
  conditioned `MULTI/EXEC`, with up to 10 attempts per record. A missing record or a newer token returns
  `FencingTokenViolation` as today; records written before it stay written.
* The SQL Server and PostgreSQL backends are unchanged (one transaction).
* Release in a 5.x minor with a change record; no data format, configuration or public API change.

### Consequences

* Good, because a Redis group settles over links whose round trip is a sizeable fraction of its heartbeat.
* Good, because operators change nothing: same keys, same ACL user.
* Bad, because a grant takes one transaction per node instead of one for all: more round trips per rebalance.
* Bad, because the grant is no longer atomic across nodes; the model and the tests must cover partial grants.
* Neutral, because an attempt can still meet the lease renewal (21 of the 75 traced attempts) or that node's own
  keep-alive; 10 attempts per record make an abort rare, not impossible. Measure it on the failing seed.

### Confirmation

* `docs/tlaplus/LeaderGlobalBarrier/RebalancerStore.tla`: `GrantedRecords` grants any subset of the allocation; TLC
  rechecks `MCSafety.cfg` and the other configurations of `docs/tlaplus/README.md`, with the results recorded there.
* A unit test that a grant failing on its second record leaves the first written and returns the violation.
* `RedisNetworkFaultTests`, seed `1196549739`, on Redis 7.0 and Valkey: 10 runs alone without a failure (it failed
  3 of 6 before), and the full Redis suite on both servers.
* PRB-0009's corrective actions reference this record.

## Sources

- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (Decision Outcome: compare-and-set transactions)
- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (the stored layout that option C would change)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (Implementation outline, item 1: the lease guard)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (the shared coordinator and its stop barrier)
- `docs/threat-models/TM-0003-redis-coordination.md` (controls: the minimal ACL command set that option B would change)
- `docs/problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md` (Root cause analysis, Evidence)
