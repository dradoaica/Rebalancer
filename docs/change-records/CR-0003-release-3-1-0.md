---
id: CR-0003
status: Draft
date: 2026-10-18
owner: "dradoaica"
approved-by: [ ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 3.1.0"
depends-on: [ "ADR-0019", "ADR-0002", "ADR-0014", "PRB-0001", "US-005", "US-006", "US-013", "CR-0002" ]
---

# Change record: Rebalancer 3.1.0 – stop unresponsive followers

## Summary

Releases 3.1.0 of all five packages with the ADR-0019 fix for PRB-0001. During a rebalance, the SQL Server and Redis
coordinators also record `StopActivity` for clients whose keep-alive expired but which still hold an assignment, so a
resumed follower stops at its first tick. No API, configuration or data format change. Release notes:
`docs/release-notes/3.1.0.md`.

## Motivation / drivers

- PRB-0001 (reproduced): after a pause longer than the keep-alive expiry, a follower kept processing resources that
  had been reassigned, until the coordinator's next rebalance. That breaks the single-owner guarantee (ADR-0002, US-013)
  for that window.

## Scope

**In scope:**

- `src/Rebalancer.SqlServer/Roles/Coordinator.cs`, `src/Rebalancer.Redis/Roles/Coordinator.cs` (identical):
  `StopUnresponsiveClientsAsync` after the stop of live clients; a non-OK result is logged, not fatal.
- `tests/Rebalancer.UnitTests/UnresponsiveFollowerTests.cs`; version 3.1.0; release notes; PRB-0001 update.

**Out of scope:**

- ADR-0019 option C (fencing token for resource handlers): needs its own decision record.
- An integration test that pauses a real follower (PRB-0001 follow-up).
- ZooKeeper (not affected).

## Impact assessment

| Area                  | Impact                                                                                                         |
|-----------------------|----------------------------------------------------------------------------------------------------------------|
| Users / customers     | A follower that resumes after a long pause gets `OnUnassignment` at its first tick, then a new assignment.     |
| Services / components | One extra read of the group's clients per rebalance, and one stop write when unresponsive clients hold resources. |
| Data / schema         | None.                                                                                                          |
| Security              | None (no new access; uses the existing fencing-guarded stop).                                                  |
| Operations            | None. Any upgrade order; mixed 3.0 / 3.1 groups behave like 3.0 when a 3.0 node coordinates.                    |

## Preconditions

- [x] Decision record in place: ADR-0019 (Accepted, `approved-by` set, on the shared trunk).
- [ ] Tests / dry-run completed on the release branch: unit tests and Docker integration tests (Redis, SQL Server).
- [ ] Rollback procedure validated / dry-run tested.
- [ ] Pre-change test suite passing cleanly.

## Implementation plan

1. Review branch `release/3.1.0`; merge into `main` and push. The push publishes 3.1.0; merging is a human action, and
   the pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" workflow run for the pushed head commit succeeds, including "Push to GitHub Packages".

## Rollback plan

Redeploy 3.0.0; no data or configuration to undo. The PRB-0001 window returns to its 3.0 length.

## Communications

Release notes `docs/release-notes/3.1.0.md`, including what the fix does not cover (a resumed process may still run
its handlers briefly; keep the keep-alive expiry above the longest expected pause).

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome:** {after change}
- **Follow-ups:** close PRB-0001 after release (its remaining items are tracked as follow-ups); ADR-0019 option C;
  pause integration test.

## Sources

- ADRs: `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`,
  `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0014-expire-stale-client-records.md`
- Problem record: `docs/problem-records/PRB-0001-resumed-follower-keeps-reassigned-resources.md`
- User stories: `docs/user-stories/US-005-coordinate-via-sql-server-backend.md`,
  `docs/user-stories/US-006-coordinate-via-redis-backend.md`,
  `docs/user-stories/US-013-cross-backend-assignment-safety-tests.md`
- Previous release: `docs/change-records/CR-0002-release-3-0-0.md`
