---
id: ADR-0026
status: Accepted
date: 2026-10-23
decides: "how a SQL Server or Redis coordinator confirms it still holds the lease before it acts, and what it keeps between tenures"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0014", "ADR-0019", "ADR-0021", "PRB-0001", "PRB-0003", "US-0005", "US-0006" ]
supersedes: [ ]
superseded-by: [ ]
---

# A SQL Server or Redis coordinator acts only under a valid lease

## Context and Problem Statement

ADR-0019 and PRB-0003 accept one window of duplicate processing after a stall longer than the keep-alive expiry: the
resumed node runs reassigned resources **until its first tick**. ADR-0021 (option D) keeps that window rather than
giving resource handlers a fencing token. Model checking the implementation (`docs/tlaplus/LeaderGlobalBarrier`,
2026-10-23) shows that a resumed **coordinator** can do worse, through four gaps in
`src/Rebalancer.SqlServer/Roles/Coordinator.cs` (mirrored in `src/Rebalancer.Redis/Roles/Coordinator.cs`):

1. **No lease check in the heartbeat.** The role task processes the coordinator event queued before the stall. The tick
   reads clients and resources but never the lease, so a coordinator whose lease was taken carries on as coordinator
   until the leadership task's next renewal is denied.
2. **A fenced-out tick keeps its handlers.** When the tick, or `StopActivityAsync`, reports a fencing violation, the
   coordinator returns before invoking `OnStop`. It stops only at a later follower tick.
3. **The grant does not check the lease.** A coordinator that stalled at the stop barrier finishes it on resuming and
   grants with its old token. `StartActivityAsync` is fenced only by client records; if the new coordinator has not
   written them yet, the grant succeeds and the old coordinator starts its own handlers. The new coordinator had
   already excluded it (stale keep-alive, record not `ResourcesGranted`, so ADR-0019 did not apply), and the overlap
   lasts until the new coordinator's next tick sees it as a new client.
4. **Caches survive tenures.** One `Coordinator` object lives as long as the provider; its `clients` and `resources`
   lists are never reset. A node that regains the lease compares the current view with the lists from its earlier
   tenure. If they match, it skips rebalancing while still running resources from that tenure that were reassigned
   since. TLC finds a behaviour, with two stalls, in which two nodes run the same resource **forever**
   (`MCLongPausesPermanent.cfg`).

All four need a stall longer than the lease / keep-alive expiry, the operating condition ADR-0021 accepts. Gaps 1–3
lengthen the accepted window; gap 4 removes its bound. This record decides how a coordinator confirms its lease before
acting and what it keeps between tenures. It does not revisit ADR-0021 (no token for resource handlers).

## Decision Drivers

* Restore the bound ADR-0019 and PRB-0003 state: a resumed node stops at its first tick.
* No double processing that lasts beyond that bound, whatever the number of stalls.
* No schema change, no public API change, and the same behaviour on SQL Server and Redis (ADR-0002).
* Reuse the fencing the code already has; add no store round trip per heartbeat.

## Considered Options

* **A. The lease guard on every coordinator action:** the coordinator's authority is the lease row naming its client id
  and fencing token. Every coordinator write carries that guard (the one ADR-0014's expiry already uses), the keep-alive
  reads it, every violation goes through the existing demotion path, and the cached view is keyed by the token.
* **B. Reset the cached view per tenure only** (gap 4).
* **C. Persisted assignment generation for resource handlers** (ADR-0021 option B).
* **D. Document only:** state the longer window and the unbounded case in PRB-0003 and the release notes.

## Decision Outcome

Chosen option: **A**, because it is the only option that restores the ADR-0019 bound in the model, and every part of it
reuses something the code already has.

The strongest argument against A is that it touches every coordinator store call to close windows that need a stall
longer than the lease expiry, which operators are told to avoid. But gap 4 turns a bounded overlap into a permanent
one, and B alone leaves gaps 1–3 open (verified: with B only, the model still finds overlaps beyond the first tick).

### Implementation outline (to be confirmed in review)

1. **One guard.** The lease row still names this coordinator and its fencing token. It is the clause
   `DeleteExpiredClientsAsync` already uses on SQL Server
   (`EXISTS (SELECT 1 FROM [RBR].[ResourceGroups] WHERE ResourceGroup = @ResourceGroup AND CoordinatorId = @CoordinatorId AND FencingToken = @FencingToken)`)
   and, on Redis, `GetLeaseIfHeldAsync` plus `Condition.HashEqual` on the lease key. Add it to `StopActivityAsync` and
   `StartActivityAsync`. No expiry time is compared and no lock is taken: another node must write the lease row before
   it can act, and acquiring the lease never touches client records, so a guarded write either happens before the
   takeover or changes nothing (the model checks the guard without an expiry clause).
2. **The keep-alive reads the lease.** The coordinator's keep-alive returns the lease row in the same command (SQL
   Server: a second `SELECT` in the batch; Redis: the lease `HGET` in the same batch), and the tick applies the guard to
   it before anything else. The tick's `FencingToken >` pre-check, dead on SQL Server because `GetActiveClientsAsync`
   does not select `FencingToken`, is then redundant and can be removed.
3. **One way to step down.** Every fencing violation, in the tick or in any write, goes through the path PRB-0001 added
   for a demoted coordinator: invoke the stop actions, set the local store to `AssignmentInProgress`, record `Waiting`,
   set `FencingTokenViolation`.
4. **The cached view is keyed by the token.** Store the fencing token with the cached clients and resources when a
   rebalance completes; a different token is a change like any other, so the first tick of a tenure rebalances. No
   reset hook is needed.

### Consequences

* Good, because a resumed coordinator stops at its first tick, and no double processing outlives it (model: no
  violation of `NoDoubleAccessOutsideResumeWindow` or `EventuallyNoDoubleAccess` with two stalls).
* Good, because each tenure starts with a rebalance, so a new coordinator never trusts state from an earlier one.
* Good, because no new mechanism: one existing guard, one existing demotion path, no extra round trip.
* Neutral, because the ADR-0019 window itself stays, as ADR-0021 decided.
* Bad, because the keep-alive batch and the stop and grant statements grow by one clause or one read each.
* Bad, because a coordinator that regains the lease always rebalances once, even when nothing changed.

### Confirmation

* `docs/tlaplus/LeaderGlobalBarrier/MCLeaseChecks.cfg` (`LeaseChecks = TRUE`, two long stalls): no error, 5,541,720
  distinct states; as built, `MCLongPauses.cfg` and `MCLongPausesPermanent.cfg` fail. Results in
  `docs/tlaplus/README.md`.
* Unit tests for each part: a tick whose keep-alive shows another lease holder stops local handlers; a grant after the
  lease was taken changes no record; a new fencing token forces a rebalance.
* Extend the paused-node integration tests (`*PausedNodeTests`) to pause the **coordinator** past the lease expiry, at
  the barrier and after granting, on both backends.

## Pros and Cons of the Options

### A. The lease guard on every coordinator action

* Good, because it closes all four gaps in the model.
* Good, because it reuses the ADR-0014 guard and the PRB-0001 demotion path; no schema or API change.
* Bad, because every coordinator store call changes, so all of them need tests.

### B. Reset the cached view per tenure only

* Good, because it is a few lines and removes the permanent case in the trace above.
* Bad, because gaps 1–3 stay: the model still finds overlaps beyond the first tick.

### C. Persisted assignment generation

* Good, because downstream systems could reject stale writes, which also covers ADR-0021's window.
* Bad, because it needs a schema change and the downstream systems' cooperation; ADR-0021 deferred it.
* Bad, because it does not stop the library from running the handlers; the overlap still happens.

### D. Document only

* Good, because no code changes.
* Bad, because operators cannot mitigate the permanent case except by avoiding stalls entirely.

## More Information

* Found with TLC on 2026-10-23; not reproduced against a real store.
* Sources: `docs/tlaplus/README.md` (LeaderGlobalBarrier findings), `docs/tlaplus/LeaderGlobalBarrier/`,
  `src/Rebalancer.SqlServer/Roles/Coordinator.cs`, `src/Rebalancer.SqlServer/Clients/ClientService.cs`,
  `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/Roles/Coordinator.cs`,
  `src/Rebalancer.Redis/Clients/ClientService.cs`.
