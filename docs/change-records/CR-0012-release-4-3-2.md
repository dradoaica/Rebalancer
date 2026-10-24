---
id: CR-0012
status: Implemented
date: 2026-10-24
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only (dradoaica, in session 2026-10-24)
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.3.2, after approval"
depends-on: [ "PRB-0006", "PRB-0007", "US-0024", "ADR-0002", "ADR-0004", "ADR-0019", "ADR-0021", "ADR-0023", "US-0003", "US-0004", "US-0019", "TM-0001", "TM-0002", "TM-0003", "CR-0011" ]
---

# Change record: Rebalancer 4.3.2 – stopped state and OnAssignmentDelay fixes

## Summary

Releases 4.3.2 of all six packages with two fixes from the coverage review of 2026-10-24: every client reports
`ClientState.Stopped` after `StopAsync` (PRB-0006), and SQL Server and Redis apply `OnAssignmentDelay` once per
assignment instead of before every heartbeat (PRB-0007). Also ships handler-level checks in the randomised SQL Server /
Redis tests (US-0024) and a TLA+ model of the delay. No public API, configuration or data format change. Release
notes: `docs/release-notes/4.3.2.md`.

## Motivation / drivers

- PRB-0007 (severity High for affected hosts): a SQL Server or Redis group whose `OnAssignmentDelay` reaches the
  keep-alive expiry never converges, reproduced on real stores; the option's documentation suggests such a delay.
- PRB-0006: `BlockAsync` can wait forever after a stop on SQL Server and Redis, and the health check reports a stopped
  client healthy; reproduced on all three backends.
- US-0024: the randomised tests missed the defect fixed in 4.3.1 because they checked only what clients report.

## Scope

**In scope:**

- `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/`: `SqlServerProvider.cs` / `RedisProvider.cs` (stopped state;
  no delay in the role loop), `Roles/Follower.cs`, `Roles/Coordinator.cs`, `Roles/ClientEvent.cs`, new internal
  `Roles/PendingStart.cs`.
- `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs` (stopped state; `GetAssignedResources()` of a terminated client).
- `src/Rebalancer.Core/ClientOptions.cs` (XML documentation of `OnAssignmentDelay`).
- Tests: `StoppedStateTests`, `AssignmentDelayTests`, `SqlServerFollowerFakes` (unit); `*PausedNodeTests.AStoppedClient_ReportsStopped`,
  `ZooKeeper/SingleClientTests.GivenSingleClient_WhenStopped_ThenReportsStopped`, `*AssignmentDelayTests`,
  `ProcessingMonitorTests`, `ProcessingMonitor`, `RandomisedScenario` (integration).
- `docs/tlaplus/LeaderGlobalBarrier/` (model and three configs), PRB-0006, PRB-0007, US-0024, this record, release
  notes, README, traceability matrix.
- Versions 4.3.2 in the six `.csproj` files; "current version" in the architecture overview and the product
  baseline; README release notes list.

**Out of scope:**

- The randomised ZooKeeper suite (unchanged code paths besides the state after termination; 2–8 hours per test).
- `*AssignmentSafetyTests` and `*PausedNodeTests` on the handler view (US-0024 non-goal).
- A CI test job (not planned; see the coverage review).

**Versioning.** A patch release: both fixes restore documented behavior, with no API, configuration or data format
change. The strongest argument for 4.4.0 instead is that hosts can observe three changes (the state after a stop,
`OnAssignment` timing with a delay, ZooKeeper's `GetAssignedResources()` after an abort); the release notes list each
under "What hosts see". The maintainer chose 4.3.2 (2026-10-24).

## Impact assessment

| Area                  | Impact                                                                                                                                                                                                                                        |
|-----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | After `StopAsync` every client reports `Stopped` (was `NotStarted` on ZooKeeper, `PendingAssignment` on SQL Server / Redis); `BlockAsync` returns; health check `Unhealthy`. With a delay: `OnAssignment` comes the full delay after a grant. |
| Services / components | No extra store calls. During a delay a node's record stays `Waiting` (granted) instead of `Active`.                                                                                                                                           |
| Data / schema         | None. Records are the same; 4.3.1 reads what 4.3.2 writes.                                                                                                                                                                                    |
| Security              | None to the threat models' assets or trust boundaries (TM-0001..0003 unchanged).                                                                                                                                                              |
| Operations            | Groups that set `OnAssignmentDelay`: upgrade every node, since a 4.3.1 node keeps delaying its keep-alives. Others: any upgrade order.                                                                                                        |

## Preconditions

- [x] Decision records: none needed (Tier 3 problem fixes; PRB-0006 and PRB-0007 approved by dradoaica, 2026-10-24).
- [x] Unit tests: 78/78.
- [x] SQL Server + Redis integration tests: 34/34 before the `ProcessingMonitor` change; after it,
  `*NetworkFaultTests` 2/2, `ProcessingMonitorTests` 5/5, `*RandomisedTests` 2/2 (seeds `1703825424`, `622234822`).
- [x] ZooKeeper integration tests on the README ensemble, not randomised: 44/44 (including `DisconnectionTests` and
  `NetworkFaultTests`).
- [x] Each regression test failed on `main` (`791919b`) and passes with the fix; with CR-0011's fix reverted, the
  randomised tests fail in the handler view on both backends (US-0024, criterion 6).
- [x] **TLC on `docs/tlaplus/LeaderGlobalBarrier`** (required by PRB-0007 before merge): run on 2026-10-24 with
  `tla2tools.jar` v1.8.0 (rev `2ef2af8`, SHA-256 `d15655eeaff4b3f7b3d65c46b838bc0f35d131b2ef4b1e473689b62c98d4726f`).
  `MCAssignmentDelay.cfg` holds (29,599,880 distinct states), `MCAssignmentDelayLongPauses.cfg` holds (15,278,706),
  `MCAssignmentDelayLiveness.cfg` holds (33,835); the five existing configs hold with unchanged state counts. A
  mutation (a follower starting a due grant without re-checking its record) violates `NoDoubleAccess`. The waiver
  dradoaica granted earlier the same day is no longer needed.
- [x] Dependency vulnerability scan (`dotnet list Rebalancer.slnx package --vulnerable --include-transitive`), as since
  CR-0009: no vulnerable package in any of the 11 projects (2026-10-24, nuget.org). No dependency changed.
- [x] Rollback procedure validated: 4.3.2 → 4.3.1 (2026-10-24, before the release). 4.3.2 (this branch) and 4.3.1 (`76a66ae`) built side by side and run as single clients against shared containers (SQL Server 2022-CU14,
  `redis:7.0`, the README's 3-node ZooKeeper ensemble); one resource `r1`, lease expiry 5 s, ZooKeeper session timeout
  10 s. Each scenario ran 4.3.2 → 4.3.1 → 4.3.2 on one group, the first 4.3.2 node ending with a clean stop or killed (`SIGKILL`) while holding `r1`; times are from each node's start to its assignment of `r1`. No warnings, errors or
  aborts were logged. The harness was a throwaway console app outside the repository.
  - **SQL Server:** clean stop: 4.3.1 after 6.3 s, 4.3.2 again after 6.3 s. Killed: 4.3.1 after 10.3 s (after the
    lease expired), 4.3.2 after 6.3 s.
  - **Redis:** clean stop: 4.3.1 after 6.2 s, 4.3.2 after 6.2 s. Killed: 4.3.1 after 10.2 s, 4.3.2 after 6.2 s.
  - **ZooKeeper, resource barrier:** clean stop: 4.3.1 after 2.1 s, 4.3.2 after 1.7 s. Killed: 4.3.1 after 12.8 s (after the session expired), 4.3.2 after 1.0 s.
  - **ZooKeeper, global barrier:** clean stop: 4.3.1 after 1.8 s, 4.3.2 after 0.4 s. Killed: 4.3.1 after 11.3 s,
    4.3.2 after 2.2 s.

  After a clean stop, the 4.3.2 nodes reported `Stopped`, the 4.3.1 nodes `PendingAssignment` (SQL Server, Redis) or
  `NotStarted` (ZooKeeper), as the release notes describe. Result: the rollback plan works as written; no data needs
  to move back.

## Implementation plan

1. Versions 4.3.2 and "current version" in the docs, and the TLC run: done on the branch (2026-10-24).
2. Merge branch `agent/prb-0006-0007-us-0024` into `main` (human) and push (human). The push publishes 4.3.2; the
   pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages".
- The published packages are version 4.3.2.

## Rollback plan

Redeploy 4.3.1; no data or configuration to undo. After a rollback, a stopped client reports `NotStarted` /
`PendingAssignment` again, and a group with an `OnAssignmentDelay` at or above its keep-alive expiry no longer
converges: lower the delay below the expiry first (PRB-0007, "Known errors / workarounds").

## Communications

Release notes `docs/release-notes/4.3.2.md` ("What hosts see" under each fix).

## Approvals

| Role                   | Name      | Date                                                 |
|------------------------|-----------|------------------------------------------------------|
| Maintainer (code)      | dradoaica | 2026-10-24 (approved in session, before the release) |
| Release / CAB (or org) | dradoaica | 2026-10-24 (approved in session, before the release) |

## Post-implementation

- **Actual outcome (2026-10-24 UTC):** `main` fast-forwarded to `41f3c04` ("Release 4.3.2") and pushed by dradoaica.
  The "Release Packages" run on that push succeeded at 20:52:21 UTC, including "Pack NuGet Packages" (20:52:14 UTC) and
  "Push to GitHub Packages" (20:52:18 UTC) (https://github.com/dradoaica/Rebalancer/actions/runs/40131669525).
  Published package versions were not listed from the agent session.
- **Deviations:**
  - The TLC run was first waived by dradoaica, then done before the merge (see Preconditions); no deviation remains.
  - dradoaica cleaned up and formatted the branch before the release commit; after that, the solution build and the
    unit tests (78/78) and `ProcessingMonitorTests` (5/5) were re-run, the integration suites were not.
- **Follow-ups:** closure review of PRB-0006 and PRB-0007; US-0024 to Done once its "Tests / checks agreed with a
  team" item is confirmed. **Done 2026-10-24:** both records closed and the story Done, confirmed by dradoaica.

## Sources

- Problem records: `docs/problem-records/PRB-0006-stopped-client-never-reports-stopped.md`,
  `docs/problem-records/PRB-0007-assignment-delay-delays-keep-alives.md`
- User stories: `docs/user-stories/US-0024-randomised-tests-check-what-nodes-process.md`, `US-0003-…`, `US-0004-…`,
  `US-0019-…`
- ADRs: `docs/adrs/0002-…`, `0004-…`, `0019-…`, `0021-…`, `0023-…`
- Threat models: `docs/threat-models/TM-0001-…`, `TM-0002-…`, `TM-0003-…` (unchanged)
- Previous release: `docs/change-records/CR-0011-release-4-3-1.md`
