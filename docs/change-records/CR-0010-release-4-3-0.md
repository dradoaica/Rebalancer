---
id: CR-0010
status: Implemented
date: 2026-10-23
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.3.0, after approval"
depends-on: [ "ADR-0026", "ADR-0027", "ADR-0028", "PRB-0005", "ADR-0002", "ADR-0003", "ADR-0014", "ADR-0019", "ADR-0021", "US-0005", "US-0006", "US-0007", "US-0010", "TM-0001", "TM-0002", "TM-0003", "CR-0009" ]
---

# Change record: Rebalancer 4.3.0 – coordination safety fixes from the TLA+ specifications

## Summary

Releases 4.3.0 of all six packages with three accepted decisions and one problem fix, all found by model checking the
coordination protocols in TLA+: a SQL Server / Redis coordinator acts only under its lease (ADR-0026); a recovered SQL
Server / Redis follower is assigned again (PRB-0005); a ZooKeeper client deletes only its own resource barriers (ADR-0027) and stops its handlers when its connection drops (ADR-0028). No public API, configuration or data format
change. Release notes: `docs/release-notes/4.3.0.md`.

## Motivation / drivers

- Two defects can let two nodes process the same resource: in ZooKeeper resource-barrier mode without any failure (ADR-0027, reproduced on a real ensemble), and on SQL Server / Redis after stalls longer than the lease expiry, in
  the worst case indefinitely (ADR-0026).
- ZooKeeper: a disconnected client's handlers kept running until about the time ZooKeeper could expire its session (ADR-0028).
- SQL Server / Redis with auto-recovery: a follower that recovered from an error was never assigned again (PRB-0005,
  reproduced on real stores).

## Scope

**In scope:**

- SQL Server, Redis: `Roles/Coordinator.cs`, `Clients/ClientService.cs`, new internal `Clients/IFencedClientService.cs`,
  `SqlServerProvider.cs` / `RedisProvider.cs` (branch `fix/prb-0005-adr-0026-coordinator-lease`).
- ZooKeeper: `Zk/ZooKeeperService.cs`, `ResourceManagement/ResourceManager.cs` (branch
  `fix/adr-0027-zookeeper-barrier-ownership`); `ResourceManagement/ResourceManager.cs`, the four role classes,
  `ZooKeeperProvider.cs` (branch `fix/adr-0028-zookeeper-suspend-on-disconnect`).
- Tests: `*CoordinatorLeaseTests`, `ZooKeeperResourceManagerTests` (unit); `*ClientRecordTests`, `*PausedNodeTests`,
  `ZooKeeper/BarrierOwnershipTests`, `ZooKeeper/DisconnectionTests` (integration).
- Versions 4.3.0; release notes; README, CONTRIBUTING, architecture overview, ZooKeeper README; TLA+ specifications (`docs/tlaplus/`); PRB-0005; traceability matrix.

**Out of scope:**

- A fencing token for resource handlers (ADR-0021, option D stays).
- The same protection for custom `IClientService` / `IZooKeeperService` implementations (the public interfaces are
  unchanged).
- The dated note on ADR-0014 that PRB-0005 lists (an edit to an accepted record; the maintainer adds it).

## Impact assessment

| Area                  | Impact                                                                                                                                                                                                                                                                                                             |
|-----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | ZooKeeper hosts get stop / start callbacks on disconnections and must keep stop handlers under about a third of the session timeout. SQL Server / Redis: a coordinator that regains the lease rebalances once; after an error, `GetCurrentState()` reports `PendingAssignment` until the client is assigned again. |
| Services / components | One more store read per SQL Server / Redis coordinator heartbeat; a lease clause on coordinator writes; one more ZooKeeper read per barrier delete.                                                                                                                                                                |
| Data / schema         | None. Records written by 4.3.0 are valid for 4.2.1 (same columns, fields and znodes).                                                                                                                                                                                                                              |
| Security              | None to the threat models' assets or trust boundaries; the fixes strengthen integrity of assignments (TM-0001..0003 unchanged).                                                                                                                                                                                    |
| Operations            | Upgrade every node of a group: mixed versions work, but the old nodes keep the old behavior (a 4.2.1 coordinator does not reassign a recovered 4.3.0 follower; a 4.2.1 ZooKeeper client can still delete another client's barrier).                                                                                |

## Preconditions

- [x] Decision records and problem record in place: ADR-0026, ADR-0027, ADR-0028 (Accepted by dradoaica, on `main`),
  PRB-0005 (root cause identified).
- [x] Unit tests: 71/71 on `fix/adr-0028-zookeeper-suspend-on-disconnect` (Release build; it contains all three
  changes).
- [x] SQL Server + Redis integration tests: 28/28 on `fix/prb-0005-adr-0026-coordinator-lease`, including the randomised
  runs; the new tests fail against the code before the fixes.
- [x] ZooKeeper integration tests: 26/26 on `fix/adr-0027-zookeeper-barrier-ownership`.
- [x] Randomised ZooKeeper tests (capped at 2 minutes each): 15/17 passed, none failed; the last 2 (`LongInterval_SmallClient_LargeResource`, both
  modes) were stopped unfinished at dradoaica's request (2026-10-23).
- [x] ZooKeeper integration tests on `fix/adr-0028-zookeeper-suspend-on-disconnect`: 28/28, including
  `DisconnectionTests` in both modes (handlers stop while the ensemble is paused and run again when the session
  survives).
- [x] TLA+: every configuration in `docs/tlaplus/` holds on the code as implemented (2026-10-23).
- [ ] Rollback procedure validated: 4.3.0 → 4.2.1 on SQL Server, Redis and ZooKeeper (clean stop and killed client).
- [x] Test suite on the release head (the code of `fix/adr-0028-zookeeper-suspend-on-disconnect`): unit 71/71, SQL
  Server + Redis integration 28/28 (including the randomised runs), ZooKeeper integration 28/28.

## Implementation plan

1. Merge the three fix branches and the release commit into `main` in order (they are chained:
   `fix/prb-0005-adr-0026-coordinator-lease` → `fix/adr-0027-zookeeper-barrier-ownership` →
   `fix/adr-0028-zookeeper-suspend-on-disconnect` → release commit) and push (human). The push publishes 4.3.0; the
   pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages".
- The six published packages are 4.3.0.

## Rollback plan

Redeploy 4.2.1; no data or configuration to undo. The four defects return; a follower record that 4.3.0 reset after
an error is read by 4.2.1 as a waiting client.

## Communications

Release notes `docs/release-notes/4.3.0.md` (upgrade every node of a group; ZooKeeper stop handlers and the session
timeout).

## Approvals

| Role                   | Name      | Date                                                      |
|------------------------|-----------|-----------------------------------------------------------|
| Maintainer (code)      | dradoaica | 2026-10-23 (recorded after the release, at their request) |
| Release / CAB (or org) | dradoaica | 2026-10-23 (recorded after the release, at their request) |

## Post-implementation

- **Actual outcome (2026-10-23):** `main` fast-forwarded to `b39c166` ("Release 4.3.0") and pushed by dradoaica. The
  "Release Packages" run on that push succeeded at 20:35:55 UTC, including "Pack NuGet Packages" and "Push to GitHub
  Packages" (20:35:51 UTC) (https://github.com/dradoaica/Rebalancer/actions/runs/40020352012). Published package
  versions were not listed from the agent session.
- **Deviations:**
  - Released while this record was still Draft, with no approvals recorded (front matter and Approvals table empty).
  - The rollback rehearsal 4.3.0 → 4.2.1 was not done before the release.
  - 2 of the 17 randomised ZooKeeper tests (`LongInterval_SmallClient_LargeResource`, both modes) were stopped
    unfinished at dradoaica's request; the other 15 passed.
- **Follow-ups:** the rollback rehearsal 4.3.0 → 4.2.1 (open). Done on 2026-10-23: approvals recorded and status set
  to Implemented (at dradoaica's request, after the release); ADR-0014 note; PRB-0005 closed.
- **Rollback rehearsal (performed after release, 2026-10-24):** the released 4.3.0 (`b39c166`) and 4.2.1 (`732ecd7`)
  built side by side from their release commits and run as single clients against shared containers (SQL Server
  2022-CU14, `redis:7.0`) and the README's 3-node ZooKeeper ensemble; one resource `r1`, lease expiry 5 s, ZooKeeper
  session timeout 10 s. Each scenario ran newer → older → newer on the same group, the newer node ending either with
  a clean stop or killed (`SIGKILL`) while holding `r1`. No errors, warnings or aborts were logged. The harness was a
  throwaway console app outside the repository.
  - **SQL Server:** clean stop: 4.2.1 assigned `r1` after 6.3 s, then 4.3.0 again after 6.3 s. Killed: 4.2.1
    assigned `r1` after 10.3 s (after the lease expired), then 4.3.0 after 6.3 s.
  - **Redis:** clean stop: 4.2.1 after 6.1 s, 4.3.0 again after 6.1 s. Killed: 4.2.1 after 10.1 s, 4.3.0 after 6.1 s.
  - **ZooKeeper, resource barrier:** clean stop: 4.2.1 after 1.1 s, 4.3.0 after 2.0 s. Killed: 4.2.1 after 8.6 s (after the session expired), 4.3.0 after 0.4 s.
  - **ZooKeeper, global barrier:** clean stop: 4.2.1 after 0.8 s, 4.3.0 after 1.4 s. Killed: 4.2.1 after 10.2 s, 4.3.0
    after 1.2 s.

  Result: the rollback plan works as written; no data needs to move back. The `db/RebalanserTables.sql` defect
  recorded in CR-0011 (rollback rehearsal) dates from this release.

## Sources

- ADRs: `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`,
  `docs/adrs/0027-zookeeper-client-deletes-only-its-own-resource-barriers.md`,
  `docs/adrs/0028-zookeeper-client-stops-handlers-when-disconnected.md`; context `docs/adrs/0002-…`, `0003-…`,
  `0014-…`, `0019-…`, `0021-…`
- Problem record: `docs/problem-records/PRB-0005-recovered-follower-never-reassigned.md`
- User stories: `docs/user-stories/US-0005-…`, `US-0006-…`, `US-0007-…`, `US-0010-…`
- Threat models: `docs/threat-models/TM-0001-…`, `TM-0002-…`, `TM-0003-…` (unchanged)
- Specifications and results: `docs/tlaplus/README.md`
- Previous release: `docs/change-records/CR-0009-release-4-2-1.md`
