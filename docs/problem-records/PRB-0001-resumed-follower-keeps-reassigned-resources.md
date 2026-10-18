---
id: PRB-0001
status: Root cause identified
date: 2026-10-18
opened: 2026-10-18
owner: ""
approved-by: [ ]            # closure review; set by a human only
severity: "High (assignment safety, short window)"
depends-on: [ "ADR-0002", "ADR-0014", "ADR-0019", "US-005", "US-006", "US-013", "CR-0003" ]
---

# Problem record: A resumed SQL Server / Redis follower keeps processing resources that were reassigned

## Summary

A follower that pauses for longer than the keep-alive expiry, but shorter than the client-record retention, is
declared dead and its resources are reassigned. When it resumes, it keeps processing its old resources until the
coordinator's next rebalance stops it. Two nodes can process the same resource during that window.

## Symptoms & impact

- **Symptoms:** after a long GC pause, VM freeze, or network stall on one node, two nodes report the same resource as
  assigned for up to about one coordinator heartbeat interval after the paused node resumes.
- **Impact:** duplicate processing of a resource for a few seconds, which breaks the core guarantee of ADR-0002 /
  US-013 in that window. It exists in 2.x and is unchanged by 3.0.

## Timeline

| When       | Event                                                                                      |
|------------|--------------------------------------------------------------------------------------------|
| 2026-10-18 | Found by code review while designing ADR-0014's re-registration safety (3.0.0 work)        |
| 2026-10-18 | Reproduced by a unit test; fix proposed in ADR-0019                                        |
| 2026-10-18 | ADR-0019 accepted; fix and regression tests implemented for 3.1.0 (CR-0003)                |
| 2026-10-18 | 3.1.0 released (CR-0003 Implemented); closure prepared for human review                    |

## Evidence

- `src/Rebalancer.SqlServer/Roles/Coordinator.cs` (identical in `src/Rebalancer.Redis/Roles/`):
  `TriggerRebalancingAsync` sends `StopActivityAsync` only to the **live** clients from `GetLiveClientsAsync`, so the
  paused node's record keeps `CoordinatorStatus = ResourcesGranted`, `ClientStatus = Active`.
- `Roles/Follower.cs`: with `ResourcesGranted` and `Active` the follower only logs its state, so a resumed node
  continues with its old assignment.
- The next coordinator heartbeat sees the node live again, the client set changes, and it rebalances. That bounds the
  window.
- Reproduced on 2026-10-18 by a unit test with an in-memory client table (SQL Server roles): the follower was granted
  `r1` and its keep-alive was 5 minutes old (expiry 1 minute). The coordinator reassigned `r1` to itself. The resumed
  follower then ran its role and invoked its stop actions 0 times (expected 1). The test was a throwaway; the
  regression test comes with the fix (ADR-0019).

## Root cause analysis

A follower decides whether it may keep processing only from its own record. Nothing tells it that the coordinator
considered it dead in the meantime. The record does not carry the fencing token or epoch of the assignment it
holds, compared with the coordinator's current one. The demoted-coordinator fix (PRB-2026-10-18) and ADR-0014's
re-registration both cover related cases, but not this one: the record has not expired yet.

### Contributing factors

- Liveness is judged by the coordinator; the follower has no lease of its own.
- The integration tests do not pause a node past the keep-alive expiry.

## Corrective actions

| Action                                                                                                                 | Owner | Status |
|------------------------------------------------------------------------------------------------------------------------|-------|--------|
| Decide the fix in a decision record                                                                                    |       | Done: ADR-0019 (Accepted), option A |
| Coordinator records stop for non-live clients that still hold an assignment (both backends)                           |       | Done (released in 3.1.0, CR-0003) |
| Regression tests: `tests/Rebalancer.UnitTests/UnresponsiveFollowerTests.cs` (fail without the fix, pass with it)       |       | Done (released in 3.1.0) |
| Integration test that pauses a real follower past the keep-alive expiry                                               |       | Open (follow-up)         |
| Optional full fix: fencing token for resource handlers (ADR-0019 option C)                                            |       | Open (needs its own decision record) |

## Preventive measures

- **Agent Governance:** N/A; code defect.
- **Tests / automation:** the pause test above.
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** until fixed, set `LeaseExpirySeconds` well above the worst expected pause, as for
  PRB-2026-10-18-demoted-coordinator-keeps-resources.
- **Decision records:** required for the fix (Tier 1: changes the coordination protocol).

## Known errors / workarounds

Keep the keep-alive expiry well above the longest expected pause of a node.

## Closure

- **Closed:** {date, set at the human closure review}
- **Resolution summary:** fixed in 3.1.0 (CR-0003, ADR-0019). During a rebalance the coordinator records
  `StopActivity` for unresponsive clients that still hold an assignment, so a resumed follower stops at its first tick.
  The window is shortened, not closed: a resumed process can still run its handlers until that tick (ADR-0019,
  release notes 3.1.0).
- **Preventive action verification:**
  - **Done:** regression tests `UnresponsiveFollowerTests` (both backends, mutation-checked) run in the unit suite.
  - **Open, to be owned at closure:** (1) an integration test that pauses a real follower past the keep-alive expiry;
    (2) ADR-0019 option C, a fencing token for resource handlers, the only full fix (needs its own decision record).
    Closing this record requires an owner for both; until then they stay in the corrective-actions table.
  - **Governance change:** N/A. This is a protocol defect, found by design review; no rule, skill or persona gap.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `docs/adrs/0014-expire-stale-client-records.md`
- `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`
- `docs/change-records/CR-0003-release-3-1-0.md`
- `docs/user-stories/US-005-coordinate-via-sql-server-backend.md`, `US-006-coordinate-via-redis-backend.md`,
  `US-013-cross-backend-assignment-safety-tests.md`
