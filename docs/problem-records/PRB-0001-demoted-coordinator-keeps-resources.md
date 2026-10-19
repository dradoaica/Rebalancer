---
id: PRB-0001
status: Closed
date: 2026-10-18
opened: 2026-10-18
owner: ""
approved-by: [ ]            # closure review; set by a human only
severity: "High (assignment safety)"
depends-on: [ "ADR-0002", "US-005", "US-006", "US-013", "CR-0001" ]
---

# Problem record: Demoted SQL Server / Redis coordinator keeps processing its resources

## Summary

With the SQL Server and Redis backends, a coordinator that loses its lease while it has resources assigned keeps
running them after another node becomes coordinator and gives those resources to other nodes.

## Symptoms & impact

- **Symptoms:** After a coordinator stall (longer than the lease expiry, e.g., GC pause or network partition), two nodes
  report the same resource as assigned. `OnAssignment` can fire on the old coordinator without a prior `OnUnassignment`.
- **Impact:** Duplicate processing of a resource (e.g., two consumers on one RabbitMQ queue), which breaks the core
  guarantee in ADR 0002 and US-013.

## Timeline

| When       | Event                                                                                     |
|------------|-------------------------------------------------------------------------------------------|
| 2026-10-18 | Detected by code review of `Roles/Coordinator.cs` and `Roles/Follower.cs` (both backends) |
| 2026-10-18 | Corrective fix and regression tests in working tree (not yet released)                    |
| 2026-10-18 | Released in 2.1.0 (`2952de6`, CR-0001)                                              |

## Evidence

- Regression tests `tests/Rebalancer.UnitTests/CoordinatorDemotionTests.cs` fail without the fix
  (`Expected: Active, Actual: Waiting`) and pass with it.
- With the fix, `RedisAssignmentSafetyTests` and `SqlServerAssignmentSafetyTests` (Docker) pass, 2026-10-18. Both
  tests now also assert that the resource is assigned at least once, so they can no longer pass when nothing is
  assigned; a run with the resource group seeded under the wrong name fails with
  `Liveness violated: the resource was never assigned to any client.`

## Root cause analysis

The coordinator ran its own start/stop actions but never wrote its own `ClientStatus` (it stayed `Waiting`). After
demotion, the next coordinator's stop barrier waits for clients that are not `Waiting`, so it did not wait for the old
coordinator. The follower role only stops a client whose status is `Active`, so the old coordinator never ran its stop
actions and later started again with new resources on top of the old ones.

### Contributing factors

- Coordinator and follower roles are duplicated per backend (ADR 0002), so the protocol is only implicit in code.
- Redis/SQL Server integration tests sample assignments but do not force a lease loss.

## Corrective actions

| Action                                                                                                                           | Owner | Status              |
|----------------------------------------------------------------------------------------------------------------------------------|-------|---------------------|
| Coordinator records `Waiting` and clears its local store after local stop, records `Active` after local start (both backends)     |       | Done (2.1.0)        |
| Regression tests for both backends                                                                                                |       | Done (2.1.0)        |
| Human review and release with a change record                                                                                     |       | Done (2.1.0, CR-0001) |

## Preventive measures

- **Agent Governance:** N/A. This is a code defect, and the existing governance (regression tests, ADR 0002 parity note)
  covers it.
- **Tests / automation:** Optional follow-up story: integration test that stalls a coordinator past lease expiry.
  Update 2026-10-19: added as `CoordinatorStalledPastLeaseExpiry_GivesUpItsResourcesAfterResume` in
  `tests/Rebalancer.IntegrationTests` (`SqlServerPausedNodeTests`, `RedisPausedNodeTests`); not released yet.
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** None.
- **Decision records:** None; the fix stays within ADR 0002.

## Known errors / workarounds

Before the fix: set `LeaseExpirySeconds` well above the worst expected pause of a node.

## Closure

- **Closed:** 2026-10-18
- **Resolution summary:** Fixed in 2.1.0 (`2952de6`, released through CR-0001): the SQL Server and Redis coordinators record their own `Waiting` / `Active` status and clear their local state when they stop, so a successor's stop barrier includes a demoted coordinator.
- **Preventive action verification:** Regression tests `CoordinatorDemotionTests` (both backends) run in the unit suite. The optional integration test that stalls a coordinator past lease expiry is not implemented and is not tracked as a story yet. Governance change: N/A (code defect covered by the existing regression-test rule).

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `docs/user-stories/US-005-coordinate-via-sql-server-backend.md`, `US-006-coordinate-via-redis-backend.md`,
  `US-013-cross-backend-assignment-safety-tests.md`
- `src/Rebalancer.Redis/Roles/Coordinator.cs`, `src/Rebalancer.SqlServer/Roles/Coordinator.cs`
