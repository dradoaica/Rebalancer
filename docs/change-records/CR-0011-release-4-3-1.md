---
id: CR-0011
status: Implemented
date: 2026-10-24
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.3.1, after approval"
depends-on: [ "ADR-0002", "ADR-0014", "ADR-0028", "US-0005", "US-0006", "TM-0002", "TM-0003", "CR-0010" ]
---

# Change record: Rebalancer 4.3.1 – StopAsync raises OnUnassignment on SQL Server and Redis

## Summary

Releases 4.3.1 of all six packages with one fix: a SQL Server or Redis client raises `OnUnassignment` on a clean
`StopAsync`, as documented and as the ZooKeeper client does, before it deletes its client record. Also adds network
fault tests for all three backends. Release notes: `docs/release-notes/4.3.1.md`.

## Motivation / drivers

- New tests that check what nodes actually process found that a SQL Server or Redis host that stops its handlers in
  `OnUnassignment` kept processing after `StopAsync`, while the coordinator reassigned its resources: two nodes could
  process the same resource. Confirmed by re-running the same seeds with a clean stop treated as the end of
  processing: no overlap remained.

## Scope

**In scope:**

- `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs` (clean-stop path).
- Tests: `*PausedNodeTests.ACleanStop_RaisesOnUnassignmentBeforeItReturns` (real SQL Server and Redis; fails before
  the fix); `Sql/SqlServerNetworkFaultTests`, `Redis/RedisNetworkFaultTests`, `ZooKeeper/NetworkFaultTests`, with the
  helpers `TcpFaultProxy`, `ProcessingMonitor`, `NetworkFaultScenarios`, `ZooKeeper/Helpers/ZkEnsemble`.
- Versions 4.3.1; release notes; README (tests, release notes list); architecture overview (current version).

**Out of scope:**

- Any change to ZooKeeper behavior (the new ZooKeeper tests cover 4.3.0 behavior and pass).

## Impact assessment

| Area                  | Impact                                                                                                                    |
|-----------------------|---------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | SQL Server / Redis hosts receive `OnUnassignment` on `StopAsync`; stop logic that also runs after `StopAsync` runs twice. |
| Services / components | None besides the callback.                                                                                                |
| Data / schema         | None.                                                                                                                     |
| Security              | None (TM-0002, TM-0003 unchanged).                                                                                        |
| Operations            | None. Any upgrade order.                                                                                                  |

## Preconditions

- [x] Unit tests: 71/71.
- [x] SQL Server + Redis integration tests: 32/32.
- [x] No ZooKeeper code change; the new `ZooKeeper/NetworkFaultTests` pass (14 cases); `ZooKeeper/DisconnectionTests`
  (moved to the shared `ZkEnsemble` helper): 2/2.
- [x] The new regression test fails against the 4.3.0 providers and passes with the fix; the ZooKeeper partition test
  fails with ADR-0028 disabled (stops after 10.4–11.0 s) and passes with it (5.0–6.4 s).
- [ ] Rollback procedure validated: 4.3.1 → 4.3.0.

## Implementation plan

1. Merge the test branch and its release commit into `main` (human) and push (human). The push publishes 4.3.1; the
   pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages".

## Rollback plan

Redeploy 4.3.0; no data or configuration to undo; `StopAsync` again stops without raising `OnUnassignment` on SQL
Server and Redis.

## Communications

Release notes `docs/release-notes/4.3.1.md` (stop logic must tolerate running twice).

## Approvals

| Role                   | Name      | Date                                                 |
|------------------------|-----------|------------------------------------------------------|
| Maintainer (code)      | dradoaica | 2026-10-24 (approved in session, before the release) |
| Release / CAB (or org) | dradoaica | 2026-10-24 (approved in session, before the release) |

## Post-implementation

- **Actual outcome (2026-10-23 UTC):** `main` fast-forwarded to `76a66ae` ("Release 4.3.1") and pushed by dradoaica.
  The "Release Packages" run on that push succeeded at 21:32:50 UTC, including "Pack NuGet Packages" and "Push to
  GitHub Packages" (21:32:47 UTC) (https://github.com/dradoaica/Rebalancer/actions/runs/40027467604). Published
  package versions were not listed from the agent session.
- **Deviations:**
  - The rollback rehearsal 4.3.1 → 4.3.0 was not done before the release.
  - The ZooKeeper integration suite was not re-run in full on the release head, since no ZooKeeper code changed; the
    new `ZooKeeper/NetworkFaultTests` (14 cases) and `ZooKeeper/DisconnectionTests` (2/2) were.
  - The same push carried `1489977`, a formatting-only commit (ADR-0014 note, CR-0010, PRB-0005, TLA+ README,
    traceability matrix; no wording change).
- **Follow-ups:** the rollback rehearsal 4.3.1 → 4.3.0 (open), together with 4.3.0 → 4.2.1 (CR-0010).

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`, `docs/adrs/0014-…`,
  `docs/adrs/0028-…` (the ZooKeeper behavior the new tests cover)
- User stories: `docs/user-stories/US-0005-…`, `US-0006-…`
- Threat models: `docs/threat-models/TM-0002-…`, `TM-0003-…` (unchanged)
- Previous release: `docs/change-records/CR-0010-release-4-3-0.md`
