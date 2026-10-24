---
id: PRB-0007
status: Closed
date: 2026-10-24
opened: 2026-10-24
owner: ""
approved-by: [ "dradoaica" ]            # closure review; set by a human only (dradoaica, in session 2026-10-24)
severity: "High (availability) for SQL Server / Redis hosts whose OnAssignmentDelay reaches the keep-alive expiry: the group never converges; others unaffected"
depends-on: [ "ADR-0002", "ADR-0004", "ADR-0019", "ADR-0021", "US-0004", "US-0005", "US-0006" ]
---

# Problem record: On SQL Server and Redis, OnAssignmentDelay delays every heartbeat tick

## Summary

`ClientOptions.OnAssignmentDelay` is documented as "a delay before invoking the OnAssignment event handler", and the
ZooKeeper provider applies it that way: once per rebalance, between the stop and the start actions. The SQL Server and
Redis providers instead wait for the full delay before **every** coordinator and follower tick, and each tick starts
with the node's keep-alive. A delay at or above the group's keep-alive expiry therefore makes live nodes look dead to
the coordinator, and the group never converges. The option's own documentation recommends such a delay (longer than
RabbitMQ's 60 s keep-alive, where rebalancing "may be triggered after only 20 seconds").

## Symptoms & impact

- **Symptoms:**
  - Keep-alives are sent at most once per `OnAssignmentDelay` (plus the tick), instead of every heartbeat interval.
  - With a delay longer than the keep-alive expiry, the nodes never settle on an assignment: the coordinator keeps
    dropping followers whose last keep-alive is older than the expiry.
  - `OnAssignment` follows a grant by anywhere between 0 and the delay, not by at least the delay, because the
    delay is spent before the node reads its grant.
  - By reading the code (not tested): stop requests and the coordinator's own ticks are delayed in the same way, so
    a rebalance takes longer, and a follower resuming from a pause (ADR-0019) can run its old resources for up to one
    more delay before its first tick.
- **Impact:** a group of SQL Server or Redis nodes configured as the documentation suggests for RabbitMQ processes
  nothing, or keeps rebalancing. Hosts that leave `OnAssignmentDelay` at zero (the default, and the repository's
  example) are unaffected. ZooKeeper is unaffected.

## Timeline

| When       | Event                                                                                                   |
|------------|---------------------------------------------------------------------------------------------------------|
| 2018-11-05 | Rebalancer 1.0 (`ef03503`): `StartRoleTask` waits `onStartDelay` before every role tick                 |
| 2026-10-24 | Found by a specification and test coverage review (only ZooKeeper tests ever set `OnAssignmentDelay`)   |
| 2026-10-24 | Reproduced on a real SQL Server and Redis; regression tests written, failing against `main` (`791919b`) |
| 2026-10-24 | Approved by dradoaica; fix and TLA+ model on branch `agent/prb-0006-0007-us-0024`; tests pass           |
| 2026-10-24 | Release prepared as 4.3.2 (CR-0012); TLC first waived, then run before merge: all configs hold          |
| 2026-10-24 | Released in 4.3.2 (CR-0012, `41f3c04`); closed after closure review by dradoaica                        |

## Evidence

- `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `StartRoleTask` (same in `src/Rebalancer.Redis/RedisProvider.cs`):
  `await WaitFor(onStartDelay, token)` before `ExecuteCoordinatorRoleAsync` and before `ExecuteFollowerRoleAsync`, for
  every client event.
- `Roles/Follower.cs`, `ExecuteFollowerRoleAsync`: the tick begins with `KeepAliveOrReRegisterAsync`.
  `Roles/Coordinator.cs`: a client is live while `TimeNow - LastKeepAlive < KeepAliveExpiryPeriod`.
- `src/Rebalancer.ZooKeeper/ResourceBarrier/Follower.cs`, `ProcessStatusChangeAsync` (and the other three ZooKeeper
  roles): the delay runs once, after the stop actions and the read of the assignment, before the start actions.
- Regression tests, run 2026-10-24 against `main` (`791919b`), all failing:
  - `tests/Rebalancer.UnitTests/AssignmentDelayTests.cs` (SQL Server follower, no Docker; keep-alive expiry 3 s,
    heartbeat 1 s, delay 4 s): `KeepAlives_AreNotDelayed_ByTheAssignmentDelay` (keep-alives 4.0 s apart);
    `OnAssignment_IsRaisedNoEarlierThanTheDelayAfterTheGrant` (`OnAssignment` 3.0 s after the grant).
  - `Sql/SqlServerAssignmentDelayTests`, `Redis/RedisAssignmentDelayTests`
    (`AssignmentDelayScenarios.ADelayLongerThanTheKeepAliveExpiry_StillConvergesAndStaysStable`: two nodes, two
    resources, lease expiry 5 s, heartbeat 1 s, delay 10 s, checked with `ProcessingMonitor`): "the nodes never
    converged within 90 s", on both backends.
- Control runs of the same scenario (2026-10-24): with no delay, SQL Server converges and stays stable (43 s); with a
  3 s delay, both SQL Server and Redis converge and stay stable for 30 s.
- The keep-alive test passes with no delay configured, so it measures the delay and not the fake's own cadence.

## Root cause analysis

The SQL Server and Redis providers implement the delay in the loop that dispatches every heartbeat event to the role,
not at the point where a node starts its granted resources. The delay is therefore paid on every tick, and the
keep-alive, which is part of the tick, inherits it. ZooKeeper does not have the problem because its delay sits in the
rebalancing response, and its session heartbeats are sent by the ZooKeeper client library.

The keep-alive gap is shown directly by the unit test. That it explains the non-convergence on a real store was the
working hypothesis when the record was opened; it is confirmed: with the fix, the same integration tests converge and
stay stable (see "Results with the fix").

### Contributing factors

- No SQL Server or Redis test sets `OnAssignmentDelay`; the only tests that do are two ZooKeeper randomised tests.
- The TLA+ models leave `OnAssignmentDelay` out (`docs/tlaplus/README.md`, both "Abstractions" lists).
- ADR-0002 accepts that the parallel implementations may drift, "mitigated only by discipline and tests"; no test
  compares how the backends apply `ClientOptions`.

## Corrective actions

| Action                                                                                                                                                                                                                                                                                                                                                                                          | Owner | Status                                                                                                            |
|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------|-------------------------------------------------------------------------------------------------------------------|
| SQL Server and Redis apply the delay once per grant, between the moment the node sees its grant and its start actions, without blocking keep-alives, stop requests or the coordinator's tick: for example, the role records when it first saw the grant and invokes the start actions on the first tick at or after that time plus the delay; a stop seen in between cancels the pending start. |       | Done; released in 4.3.2 (see Design decisions)                                                                    |
| Regression tests listed under Evidence                                                                                                                                                                                                                                                                                                                                                          |       | Pass with the fix (see Design decisions)                                                                          |
| Model `OnAssignmentDelay` in `docs/tlaplus/LeaderGlobalBarrier` before the fix is merged: the deferred start must keep `NoDoubleAccess` and the ADR-0019 resume window, and must not let the coordinator grant while a deferred start is pending for a resource it reassigns                                                                                                                    |       | Done: TLC holds on all three delay configs; existing configs unchanged (2026-10-24; see `docs/tlaplus/README.md`) |
| `ClientOptions.OnAssignmentDelay` XML documentation and release notes: the delay runs between a grant and `OnAssignment`; until the fix, keep it below the group's keep-alive expiry                                                                                                                                                                                                            |       | XML documentation done; release notes drafted (4.3.2, CR-0012)                                                    |

**Design questions for review** (the corrective action must answer them before implementation):

1. When does a follower record `Active`: when it sees the grant, or when its start actions run? If at the grant, a
   rebalance during the delay waits for a node that is not running anything; if at the start, the coordinator's view
   of "active" changes meaning for the duration of the delay.
2. Is the delay measured from the grant (as ZooKeeper does, after reading the assignment) or from the stop? ZooKeeper
   measures from the read, so this record proposes the same.

**Tier: 3** – a bug fix within ADR-0002 and ADR-0004: the option already means "a delay before OnAssignment"; no public
API, schema or configuration change. The reviewer confirms or corrects the tier. Hosts see `OnAssignment` later after a
grant than today (by up to one delay), which matches the documentation.

**Approval (2026-10-24).** dradoaica approved this record in session on 2026-10-24, including its Tier 3 classification,
and asked for the fix and tests to proceed. Recorded at their request. The design questions are answered below ("Design decisions").

### Design decisions (2026-10-24, as implemented; for review in the MR/PR)

1. **`Active` is recorded when the start actions run**, not when the grant is seen. During the delay the node runs
   nothing, so a rebalance need not wait for it, and "Active" keeps meaning "its handlers run". The coordinator does
   the same for its own share: its record stays `Waiting` (granted) until its handlers start.
2. **The delay is measured from when the node sees its grant**, as on ZooKeeper, which measures from reading the
   assignment.
3. **Mechanism.** `Roles/PendingStart.cs` (both backends) holds the grant a node has seen: its resources, fencing token
   and a stopwatch. A follower tick that still reads the same grant (same resources, same token, `Waiting`) after the
   delay starts it; any other record state cancels it, and a changed grant restarts the delay. The coordinator starts
   its own share on a tick that triggers no rebalance, under the tenure that granted it. A rebalance, a step-down, a
   role change (the provider cancels the other role's pending start) and an error (`InvokeOnStop`) cancel it. The
   per-tick wait in `StartRoleTask` is gone; the delay travels with each `ClientEvent`, as the record retention does.
4. **Known limit.** A grant revoked and re-granted with the same resources and token between two ticks of the same
   node is taken for the same grant, so its delay is not restarted. A rebalance waits at least 5 s at its barrier, so
   this needs a node that misses ticks for that long, the stall that ADR-0019 / ADR-0021 already accept.

**Results with the fix (2026-10-24).** Unit tests 78/78, including the four `AssignmentDelayTests` (three added for the
new mechanism: a stop during the delay cancels the follower's start; the coordinator's own share starts after the delay
and its record stays `Waiting` until then; a step-down cancels it). The stop-during-delay test also passes against the
old code, which never started early; it guards the new cancellation, not the original bug. Integration:
`*AssignmentDelayTests` pass on SQL Server and Redis (10 s delay, 5 s expiry: converged and stable for 30 s), and the
other SQL Server / Redis integration tests pass (34/34 in all).

## Preventive measures

- **Agent Governance:** N/A; a code defect, not a gap in rules, skills or personas.
- **Tests / automation:** the regression tests above; the TLA+ model of the delay (`MCAssignmentDelay*.cfg`, all
  holding; a mutation check confirms they can fail).
- **Monitoring / alerts:** N/A (library). A node left waiting for an assignment longer than the health check's
  pending threshold is reported `Degraded` (ADR-0023); not verified for this scenario.
- **Runbooks / docs:** the XML documentation and release notes (above).
- **Decision records:** none needed at Tier 3.

## Known errors / workarounds

Keep `OnAssignmentDelay` below the group's keep-alive expiry (the lease expiry), with at least one heartbeat interval to
spare: verified with a 3 s delay and a 5 s expiry. A host that needs a longer delay (for example the RabbitMQ case in
the option's documentation) can raise the group's `LeaseExpirySeconds` above the delay, at the cost of slower
failover, or delay its own start logic inside its `OnAssignment` handler.

## Closure

- **Closed:** 2026-10-24 (closure review: dradoaica, in session)
- **Resolution summary:** fixed in 4.3.2 (CR-0012, `41f3c04`). SQL Server and Redis apply `OnAssignmentDelay` once per
  assignment, between the node seeing it and its start actions (`Roles/PendingStart.cs`); keep-alives and stop
  requests are no longer delayed, and a group with a delay longer than its keep-alive expiry converges.
- **Preventive action verification:**
  - **Done:** regression tests `AssignmentDelayTests` (unit) and `*AssignmentDelayTests` (real SQL Server and Redis),
    which failed on `791919b` and pass with the fix.
  - **Done:** TLA+ model of the delay; `MCAssignmentDelay.cfg`, `MCAssignmentDelayLongPauses.cfg` and
    `MCAssignmentDelayLiveness.cfg` hold, the existing configs keep their state counts, and a mutation check fails as
    expected (`docs/tlaplus/README.md`).
  - **Done:** `ClientOptions.OnAssignmentDelay` XML documentation and the 4.3.2 release notes.
  - **Governance change:** N/A. A code defect with a test gap, not a gap in rules, skills or personas.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `docs/adrs/0004-client-options-recovery-and-assignment-delay.md` (Decision Outcome)
- `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`,
  `docs/adrs/0021-assignment-generation-for-resource-handlers.md` (the resume window)
- `docs/user-stories/US-0004-configure-recovery-and-assignment-delay.md`,
  `US-0005-coordinate-via-sql-server-backend.md`, `US-0006-coordinate-via-redis-backend.md`
- `docs/tlaplus/README.md` (Abstractions)
- `src/Rebalancer.Core/ClientOptions.cs`, `src/Rebalancer.SqlServer/SqlServerProvider.cs`,
  `src/Rebalancer.SqlServer/Roles/Follower.cs`, `src/Rebalancer.SqlServer/Roles/Coordinator.cs`, the Redis
  counterparts, `src/Rebalancer.ZooKeeper/ResourceBarrier/Follower.cs`
