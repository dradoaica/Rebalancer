---
id: CR-0005
status: Implemented
date: 2026-10-19
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 3.2.0"
depends-on: [ "ADR-0020", "ADR-0021", "US-0016", "PRB-0001", "PRB-0003", "TM-0001", "TM-0002", "CR-0004" ]
---

# Change record: Rebalancer 3.2.0 – Redis 4.0 deprecation warning, operator scripts, paused-node tests

## Summary

Releases 3.2.0 of all five packages. The only behavior change: a Redis client logs one warning per session that 4.0
moves its group to per-group keys (US-0016, ADR-0020). Also ships the SQL Server least-privilege role script, the
ZooKeeper znode runbook, and new integration tests. No API, configuration or data format change. Release notes:
`docs/release-notes/3.2.0.md`.

## Motivation / drivers

- ADR-0020 requires a 3.x release that warns before 4.0 changes the Redis key layout.
- TM-0001 and TM-0002 follow-ups (operator procedures); PRB-0003 and PRB-0001 test follow-ups.

## Scope

**In scope:**

- `src/Rebalancer.Redis/RedisProvider.cs` (`StartAsync`: one `Warn`); version 3.2.0 in all five packages.
- `docs/release-notes/migrating-to-4.0.md`, `docs/release-notes/3.2.0.md`, `db/RebalanserLeastPrivilege.sql`,
  `src/Rebalancer.ZooKeeper/README.md` (runbook).
- Tests: `PerGroupKeysDeprecationTests` (unit); `SqlServerPausedNodeTests`, `RedisPausedNodeTests`,
  `SqlServerLeastPrivilegeTests` (integration, Docker).
- Documentation changes since 3.1.0: CR/PRB renumbering, traceability matrix, ADR-0021 (option D), stories
  US-0017..0019 (Draft).

**Out of scope:**

- The 4.0 key layout itself (ADR-0020; separate branch and change record).
- A CI test job (not planned).

## Impact assessment

| Area                  | Impact                                                                                                     |
|-----------------------|------------------------------------------------------------------------------------------------------------|
| Users / customers     | Redis users see one warning per client start in their logs. Nothing else changes.                          |
| Services / components | None at runtime besides the log line.                                                                      |
| Data / schema         | None. The role script is optional and additive (a new database role).                                      |
| Security              | Positive: operators get a least-privilege role and a verified procedure to secure ZooKeeper znodes.        |
| Operations            | None. Any upgrade order; 3.1 and 3.2 nodes can run in one group.                                           |

## Preconditions

- [ ] Decision record in place: ADR-0020 (Accepted, on the shared trunk); ADR-0021 Accepted on branch
  `agent/open-topics-2026-10-19`, not yet on the shared trunk.
- [x] US-0016 readiness approval (`approved-by`) recorded (dradoaica, 2026-10-19).
- [x] Tests / dry-run completed on the release branch (2026-10-19, on top of `3f6360d`): unit 49/49; SQL Server + Redis
  integration 13/13 (Docker); ZooKeeper integration not run (no ensemble; no ZooKeeper code change).
- [ ] Rollback procedure validated: redeploy 3.1.0 (no data change); not rehearsed.
- [x] Pre-change test suite passing cleanly (`Rebalancer.Redis` builds with warnings as errors).

Approved by dradoaica on 2026-10-19 with two preconditions open: ADR-0021 reaches the shared trunk with this
release's merge, and the rollback (redeploy 3.1.0) is not rehearsed.

## Implementation plan

1. Review branch `agent/open-topics-2026-10-19`; merge into `main` and push. The push publishes 3.2.0; merging is a
   human action, and the pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" workflow run for the pushed head commit succeeds, including "Push to GitHub Packages".
- A Redis client of 3.2.0 logs the 4.0 warning once at start.

## Rollback plan

Redeploy 3.1.0; no data or configuration to undo. The role script's role can stay or be dropped
(`DROP ROLE [rebalancer_node]` after removing its members).

## Communications

Release notes `docs/release-notes/3.2.0.md`; migration guide `docs/release-notes/migrating-to-4.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-10-19 |
| Release / CAB (or org) | dradoaica | 2026-10-19 |

## Post-implementation

- **Actual outcome (2026-10-19):** `agent/open-topics-2026-10-19` merged into `main` as `860b3d3` ("Release 3.2.0"),
  merged locally by the agent at dradoaica's request and pushed by dradoaica. The "Release Packages" run on that push
  succeeded at 18:22:09 UTC, including "Push to GitHub Packages"
  (https://github.com/dradoaica/Rebalancer/actions/runs/39301814641). Published package versions were not listed from
  the agent session.
- **Deviations:** two preconditions were open at approval (recorded under Preconditions): ADR-0021 reached the shared
  trunk with this push, and the rollback to 3.1.0 was not rehearsed.
- **Follow-ups:** 4.0.0 (CR-0006, ADR-0020's layout change) on `agent/redis-4.0-per-group-keys`, after its approvals
  are recorded.

## Sources

- ADRs: `docs/adrs/0020-per-group-redis-leases-and-resources.md`,
  `docs/adrs/0021-assignment-generation-for-resource-handlers.md`
- User story: `docs/user-stories/US-0016-warn-redis-groups-about-per-group-keys-in-4-0.md`
- Problem records: `docs/problem-records/PRB-0001-demoted-coordinator-keeps-resources.md`,
  `docs/problem-records/PRB-0003-resumed-follower-keeps-reassigned-resources.md`
- Threat models: `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md`,
  `docs/threat-models/TM-0002-sql-server-coordination.md`
- Previous release: `docs/change-records/CR-0004-release-3-1-0.md`
