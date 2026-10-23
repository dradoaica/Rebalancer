---
id: PRB-0005
status: Root cause identified
date: 2026-10-23
opened: 2026-10-23
owner: "dradoaica"
approved-by: [ ]            # closure review; set by a human only
severity: "Medium (availability: a recovered follower never takes part again; no double processing)"
depends-on: [ "ADR-0002", "ADR-0014", "ADR-0023", "ADR-0026", "US-0005", "US-0006", "US-0021" ]
---

# Problem record: A SQL Server / Redis follower that recovers from an error is never assigned resources again

## Summary

With `ClientOptions.AutoRecoveryOnError`, a SQL Server or Redis client restarts after an internal error. If it is a
follower, it never receives resources again: its record stays `Terminated`, and no coordinator includes a `Terminated`
client. The follower keeps sending keep-alives, so the record never expires either. It takes part again only if it
becomes the coordinator, or if its process restarts.

## Symptoms & impact

- **Symptoms:** after an error on a follower (for example a store timeout), `OnAborted` is raised and the client
  restarts, but `OnAssignment` never follows. The health check (ADR-0023) reports `Unhealthy` from then on.
  `GetCurrentState()` / `GetAssignedResources()` keep reporting the assignment from before the error (state `Assigned`,
  old resource list), although the stop actions ran and no handler runs.
- **Impact:** lost capacity: the group runs on fewer nodes, and if every follower hits an error, the coordinator runs
  every resource. No resource is run twice. It contradicts the documented contract: `RebalancerClient.OnAborted` says a
  SQL Server or Redis client with auto-recovery "restarts ... and takes part again (`OnAssignment` follows)", and
  ADR-0014's context says a recovering node moves its record back to `Waiting`, which only a coordinator does.
  Auto-recovery is off by default, so only hosts that enable it are affected.

## Timeline

| When       | Event                                                                                                   |
|------------|---------------------------------------------------------------------------------------------------------|
| 2026-10-23 | Found by model checking (`docs/tlaplus/LeaderGlobalBarrier/MCRecoveredFollower.cfg`); confirmed in code |
| 2026-10-23 | Fix designed and checked in the model (see Corrective actions); not implemented                        |

## Evidence

- `src/Rebalancer.SqlServer/SqlServerProvider.cs` (same in `src/Rebalancer.Redis/RedisProvider.cs`): after a faulted
  task the restart path calls `SetClientStatusAsync(clientId, ClientStatus.Terminated)` (comment: "the record is reused
  (Terminated -> Waiting)").
- `Roles/Coordinator.cs`: only the coordinator moves **its own** record from `Terminated` to `Waiting`
  (`self.ClientStatus == ClientStatus.Terminated` branch).
- `Roles/Follower.cs`: a follower acts only on `StopActivity` + `Active` or `ResourcesGranted` + `Waiting`; with
  `Terminated` it does nothing.
- `Clients/ClientService.cs`: `GetActiveClientsAsync` returns only `Waiting` and `Active` records, so a `Terminated`
  follower is never in a coordinator's client set.
- `NotifyOfError` → `InvokeOnStop` invokes the stop actions but does not reset the local `ResourceGroupStore`, hence
  the stale state.
- TLC counterexample (2026-10-23): one store error on a follower; `EventuallyConverged` is violated.
- Not reproduced against a real store.

## Root cause analysis

The restart path marks the record `Terminated` and relies on the role that runs next to move it back to `Waiting`. Only
the coordinator role does that; the follower role was never given the same step. Because a `Terminated` record is not
live, the coordinator sees no change when the follower comes back, so nothing triggers a rebalance that would include
it.

### Contributing factors

- The integration tests use auto-recovery but inject no error into a follower and then check that it is assigned
  again. `RedisAbortedStateTests.WithAutoRecovery_AFailingClientIsNotAborted` only checks the state is not `Aborted`,
  with a client service that never assigns anything.
- ADR-0014 stated the recovery behaviour as general, which hid the gap.

## Corrective actions

| Action                                                                                                                                                                                                                                   | Owner | Status                                                         |
|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------|----------------------------------------------------------------|
| On restart, write the record as a new registration would (`Waiting`, `StopActivity`, no resources, fencing token kept) instead of `Terminated`, and reset the local store to `AssignmentInProgress` (both backends)                      |       | Proposed (checked in the model)                                |
| The coordinator rebalances when a live client is `Waiting` under `StopActivity` (stopped and not granted), so a restart between two coordinator ticks is not missed                                                                   |       | Proposed (checked in the model)                                |
| Regression tests: a follower that hits an error with auto-recovery is assigned again and raises `OnAssignment` (unit, both backends; integration with an injected follower error)                                                     |       |                                                                |
| Remove the now unused `Terminated` handling for restarts (`Coordinator.cs`), keeping `Terminated` only where `IClientService.DeleteClientAsync`'s default still uses it                                                              |       |                                                                |
| Note in ADR-0014 (dated, below the record) that the recovery sentence was true for the coordinator only until this fix                                                                                                             |       |                                                                |

Tier: 3 (a bug fix within ADR-0002 and ADR-0014; no schema or public API change). The reviewer confirms or corrects the
tier. The model shows why both actions are needed: resetting the status to `Waiting` alone is **unsafe**, because the
record still grants the follower its old resources, which were reassigned meanwhile, and its next tick restarts them.

Checked with TLC on 2026-10-23 in a variant of `LeaderGlobalBarrier.tla` with both actions: one and two store errors,
liveness holds (15,727 and 91,588 distinct states); safety without long stalls holds (12,956,398); with ADR-0026 and
long stalls, the resume window holds (5,437,209). The variant is not in the repository yet; it belongs in
`LeaderGlobalBarrier.tla` with the fix.

## Preventive measures

- **Agent Governance:** N/A; a code defect, not a gap in rules, skills or personas.
- **Tests / automation:** the regression tests above; keep `MCRecoveredFollower.cfg` in the TLA+ results until the fix
  ships, then flip its expectation.
- **Monitoring / alerts:** hosts that use the health check (ADR-0023) already see the follower as `Unhealthy`.
- **Runbooks / docs:** until fixed, restart the process of a follower that stays `Unhealthy` after `OnAborted`, or
  run without auto-recovery and restart on `ClientState.Aborted`.
- **Decision records:** none needed (Tier 3).

## Known errors / workarounds

Restart the affected process: it registers a new client id and is assigned again. Without auto-recovery, a client that
gives up reports `ClientState.Aborted` (US-0021), which a supervisor can act on.

## Related

The SQL Server `GetActiveClientsAsync` does not select `FencingToken`, so the coordinator tick's `FencingToken >`
pre-check never fires on SQL Server. It is a separate defect with a different root cause; ADR-0026 (Accepted) makes
that pre-check redundant and removes it, so it is tracked there, not here.

## Closure

- **Closed:** {YYYY-MM-DD}
- **Resolution summary:** {…}
- **Preventive action verification:** {…}

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `docs/adrs/0014-expire-stale-client-records.md` (Context and Problem Statement)
- `docs/adrs/0023-health-check-for-a-rebalancer-client.md`
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`
- `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md`, `US-0006-coordinate-via-redis-backend.md`,
  `US-0021-report-aborted-state-on-sql-server-and-redis.md`
- `docs/tlaplus/README.md` (LeaderGlobalBarrier, finding 1), `docs/tlaplus/LeaderGlobalBarrier/MCRecoveredFollower.cfg`
- `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.SqlServer/Roles/Coordinator.cs`,
  `src/Rebalancer.SqlServer/Roles/Follower.cs`, `src/Rebalancer.SqlServer/Clients/ClientService.cs`, and the Redis
  counterparts; `src/Rebalancer.Core/RebalancerClient.cs` (`OnAborted` remarks)
