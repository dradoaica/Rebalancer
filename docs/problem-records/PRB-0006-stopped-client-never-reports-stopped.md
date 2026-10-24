---
id: PRB-0006
status: Root cause identified
date: 2026-10-24
opened: 2026-10-24
owner: ""
approved-by: [ ]            # closure review; set by a human only
severity: "Medium (a stopped SQL Server / Redis client is reported Healthy, BlockAsync can wait forever; no double processing)"
depends-on: [ "ADR-0013", "ADR-0023", "US-0003", "US-0018", "US-0021", "CR-0011" ]
---

# Problem record: A client stopped with StopAsync never reports ClientState.Stopped

## Summary

`ClientState.Stopped` is documented as "the client has been stopped in a controlled way", but no backend reports it
from `GetState()`. After `StopAsync`, a SQL Server or Redis client reports `PendingAssignment` (since 4.3.1; before,
its last assignment as `Assigned`), and a ZooKeeper client reports `NotStarted`. Two documented behaviors that depend
on the state therefore do not hold: `RebalancerClient.BlockAsync` returning when the client stops, and the health
check (ADR-0023) reporting a stopped client `Unhealthy`.

## Symptoms & impact

- **Symptoms:**
  - SQL Server and Redis: `GetCurrentState()` and `GetAssignedResources().ClientState` return `PendingAssignment`
    after `StopAsync` (4.3.1); in 4.3.0 and earlier they kept returning `Assigned` with the old resources.
  - ZooKeeper: `NotStarted` after `StopAsync`, the state of a client that was never started.
  - `BlockAsync(token, maxStopTime)` waits while the state is `PendingAssignment` or `Assigned`, so on SQL Server and
    Redis it never returns when the client is stopped by another caller (for example a host's shutdown path calling
    `StopAsync` while a worker waits in `BlockAsync` with a different token).
  - The health check reports a stopped SQL Server / Redis client `Healthy`, then `Degraded` after the pending
    threshold, instead of `Unhealthy` (ADR-0023, Decision Outcome table). On ZooKeeper the result is right (`NotStarted` is also `Unhealthy`), for the wrong state.
- **Impact:** a host can hang on shutdown, and an orchestrator reading the health check does not see a stopped node.
  No resource is processed twice: the handlers do stop (CR-0011). Hosts that wait with `BlockAsync` on the same token
  they cancel are unaffected, because `BlockAsync` then calls `StopAsync` itself and returns.

## Timeline

| When       | Event                                                                                                            |
|------------|------------------------------------------------------------------------------------------------------------------|
| 2018-11-05 | Rebalancer 1.0 (`ef03503`): `Stopped` returned only by `GetAssignedResources(CancellationToken)` on cancellation |
| 2026-10-24 | Found by a specification and test coverage review: no test asserts a real client's state after `StopAsync`       |
| 2026-10-24 | Regression tests written; they fail against `main` (`791919b`) on all three backends (see Evidence)              |
| 2026-10-24 | Approved by dradoaica; fix on branch `agent/prb-0006-0007-us-0024`; the regression tests pass                    |
| 2026-10-24 | Release prepared as 4.3.2 (CR-0012, Submitted); not released                                                     |

## Evidence

- `src/Rebalancer.SqlServer/SqlServerProvider.cs` (same in `src/Rebalancer.Redis/RedisProvider.cs`):
  `InvokeOnStopForCleanStop` sets the store to `AssignmentInProgress`, and `GetState(AssignmentStatus)` maps it to
  `PendingAssignment`; no field records a clean stop.
- `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`: termination sets `started = false`, so `GetState()` falls through
  to `NotStarted` unless the client aborted.
- `src/Rebalancer.Core/RebalancerClient.cs`, `BlockAsync`: returns only for states other than `PendingAssignment` and
  `Assigned`.
- `tests/Rebalancer.UnitTests/RebalancerClientHealthCheckTests.cs`: the `Stopped` case uses a fake provider that
  returns `Stopped`, which no real provider does.
- Regression tests, run 2026-10-24 against `main` (`791919b`), all failing:
  - `tests/Rebalancer.UnitTests/StoppedStateTests.cs` (SQL Server, no Docker):
    `AStoppedClient_ReportsStopped_WithNoResources` (expected `Stopped`, actual `PendingAssignment`);
    `BlockAsync_ReturnsOnceTheClientIsStoppedElsewhere` (still waiting 5 s after `StopAsync`).
  - `*PausedNodeTests.AStoppedClient_ReportsStopped` (real SQL Server and Redis): actual `PendingAssignment`.
  - `ZooKeeper/SingleClientTests.GivenSingleClient_WhenStopped_ThenReportsStopped` (both modes, README ensemble):
    actual `NotStarted`.
- The unit tests pass against a throwaway prototype of corrective action 1 on SQL Server (a `stopped` flag set after a
  clean stop, checked after `aborted`); the prototype was discarded.

## Root cause analysis

No provider keeps a "stopped" state. SQL Server and Redis derive the state from the local assignment status only, which
a clean stop resets to "assignment in progress" (since CR-0011, so that the coordinator reassigns only resources nobody
processes). ZooKeeper resets `started`, which `GetState()` reads as "never started". `Stopped` was added to the enum in
1.0 without a provider that reports it, and `BlockAsync` and the health check were later written against the enum's
documentation rather than against a provider's behavior.

### Contributing factors

- No test asserts the state of a real client after `StopAsync`. US-0003's criteria cover how `StopAsync` waits, not
  what the client reports afterwards.
- The health-check tests use a fake provider, so they confirm the mapping, not that the mapped state occurs.
- The three backends report different states for the same situation, and nothing compares them.

## Corrective actions

| Action                                                                                                                                                                                                                                                                                                    | Owner | Status                                           |
|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------|--------------------------------------------------|
| All three providers record a clean stop once their main loop has ended on cancellation; `GetState()` then returns `Stopped`, and `GetAssignedResources()` returns `Stopped` with no resources. `Aborted` takes precedence (a client that gave up stays `Aborted`). `IRebalancerProvider` does not change. |       | Done on branch, not released                     |
| Regression tests listed under Evidence                                                                                                                                                                                                                                                                    |       | Pass with the fix                                |
| Release notes: after `StopAsync`, `GetCurrentState()` returns `Stopped` on every backend (ZooKeeper hosts that compare with `NotStarted`, and SQL Server / Redis hosts that compare with `PendingAssignment`, must update)                                                                                |       | Drafted: `docs/release-notes/4.3.2.md` (CR-0012) |

**Design note for review.** The strongest alternative is to fix it once in `RebalancerClient` (report `Stopped` when
its own `stopped` flag is set), which also covers custom providers. It is not proposed because `StopAsync(timeout)`
sets that flag when the timeout elapses, while the provider may still be running its handlers; the client would then
claim `Stopped` for a node that still processes. The provider knows when its loop has ended.

**Tier: 3** – a bug fix within existing decisions: it makes the providers report the state that the public enum,
`BlockAsync` and accepted ADR-0023 already document; no public API, schema or configuration change. The reviewer
confirms or corrects the tier. The change is visible to hosts that compared with `NotStarted` (ZooKeeper) or
`PendingAssignment` after a stop; if the reviewer treats that as a contract change, it becomes Tier 1 and needs an
ADR first.

**Approval (2026-10-24).** dradoaica approved this record in session on 2026-10-24, including its Tier 3 classification,
and asked for the fix and tests to proceed; design as proposed (per-provider fix). Recorded at their request.

**As implemented (2026-10-24).** SQL Server and Redis set a `stopped` flag when their main loop ends on cancellation
without having given up (`aborted`); ZooKeeper sets it in `TerminateAsync` for every termination that is not an abort (all of them are cancellations). `GetState()` checks `aborted`, then `stopped`. `GetAssignedResources()` of a
terminated ZooKeeper client now also reports its terminal state with no resources: besides `Stopped`, an aborted
ZooKeeper client now reports `Aborted` there (it reported `PendingAssignment`), as SQL Server and Redis do since
US-0021; the 4.3.2 release notes list it. Results: unit tests 78/78; `*PausedNodeTests.AStoppedClient_ReportsStopped`
passes on SQL Server and Redis, with the other SQL Server / Redis integration tests (34/34). On the README's ZooKeeper
ensemble, `SingleClientTests.GivenSingleClient_WhenStopped_ThenReportsStopped` passes in both modes, with the rest of
the non-randomised ZooKeeper suite (44/44, including `DisconnectionTests` and `NetworkFaultTests`).

## Preventive measures

- **Agent Governance:** N/A; a code defect with a test gap, not a gap in rules, skills or personas.
- **Tests / automation:** the regression tests above, on all three backends; a health-check test with a real
  provider would have caught it as well.
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** release notes (above).
- **Decision records:** none needed at Tier 3.

## Known errors / workarounds

- To wait for a node to stop, await `StopAsync` rather than `BlockAsync`, or pass `BlockAsync` the token that triggers
  the stop (it then calls `StopAsync` itself and returns).
- Stop health reporting before stopping the client (ADR-0023 notes that hosts normally do so for a deliberate
  shutdown), or treat the host's own "stopping" flag as unhealthy.

## Closure

- **Closed:** –
- **Resolution summary:** –
- **Preventive action verification:** –

## Sources

- `docs/adrs/0013-explicit-providers-only-remove-static-registry.md` (a client always has a provider)
- `docs/adrs/0023-health-check-for-a-rebalancer-client.md` (Decision Outcome, behavior table)
- `docs/user-stories/US-0003-graceful-shutdown-and-read-assignment-state.md`,
  `US-0018-report-client-health-to-the-host.md`, `US-0021-report-aborted-state-on-sql-server-and-redis.md`
- `docs/change-records/CR-0011-release-4-3-1.md` (clean-stop path)
- `src/Rebalancer.Core/ClientState.cs`, `src/Rebalancer.Core/RebalancerClient.cs` (`BlockAsync`),
  `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs`,
  `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`,
  `src/Rebalancer.Extensions.Diagnostics.HealthChecks/RebalancerClientHealthCheck.cs`
