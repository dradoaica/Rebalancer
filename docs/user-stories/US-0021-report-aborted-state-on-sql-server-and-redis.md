---
id: US-0021
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19)
priority: "Should"
ssdlc-phase: Coding
depends-on: [ "ADR-0002", "ADR-0023", "US-003", "US-005", "US-006", "US-0018" ]
---

# User story: SQL Server and Redis clients report Aborted when they give up

## Story

**As a** host developer  
**I want** `GetCurrentState()` to return `ClientState.Aborted` once a SQL Server or Redis client has given up after an
error  
**So that** my code, `BlockAsync` and health checks can see that the node no longer takes part in its group

## Context

Found while drafting ADR-0023 (2026-10-19): only the ZooKeeper provider reports `Aborted`. After a non-recoverable
error (auto-recovery off), the SQL Server and Redis providers kept reporting their last state (`Assigned` or
`PendingAssignment`), so `RebalancerClient.BlockAsync` waited forever and a state-only check saw a healthy node.

Also found (2026-10-19): SQL Server and Redis raise `OnAborted` for **every** error, including ones they recover from
with auto-recovery on, although `RebalancerClient`'s documentation says "non recoverable". The 4.1.0 health check (US-0018) therefore stayed `Unhealthy` after a recovered error; it now clears the abort when the client is assigned
again. Changing when `OnAborted` fires is a public behavior change and is not part of this story.

## Acceptance criteria

1. With `AutoRecoveryOnError = false`, a SQL Server or Redis client whose role or leadership task fails reports
   `ClientState.Aborted` from `GetCurrentState()` and `GetAssignedResources()` (with no resources).
2. With auto-recovery on, a failing client keeps retrying and never reports `Aborted`.
3. `BlockAsync` returns for an aborted client.
4. The health check (ADR-0023) is `Healthy` again after `OnAborted` once the client is assigned again.

## Non-goals

- Changing when SQL Server and Redis raise `OnAborted` (only for final errors, as documented and as ZooKeeper does).
- ZooKeeper (already reports `Aborted`).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0023-health-check-for-a-rebalancer-client.md`
- Other stories: `docs/user-stories/US-003-graceful-shutdown-and-read-assignment-state.md`,
  `docs/user-stories/US-0018-report-client-health-to-the-host.md`

## Security & compliance

None: state reporting only.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – `SqlServerProvider.cs`, `RedisProvider.cs` (`aborted`), `RebalancerClientHealthCheck.cs`;
  tests `AbortedStateTests` (unit, SQL Server), `RedisAbortedStateTests` (integration, Redis),
  `RebalancerClientHealthCheckTests.AnAbortFollowedByANewAssignment_IsHealthyAgain` (fails without the fix)
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-19)
- [x] Docs / runbooks updated if needed – release notes 4.2.0
- [x] Traceable to change record (if released) - `docs/change-records/CR-0008-release-4-2-0.md`

## Sources

- `docs/adrs/0023-health-check-for-a-rebalancer-client.md` (Context, findings 2; More Information)
- `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs` (`GetState`, `EvaluateTerminationAsync`)
- `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs` (`NotifyOfError`)
