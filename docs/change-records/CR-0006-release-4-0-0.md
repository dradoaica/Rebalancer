---
id: CR-0006
status: Implemented
date: 2026-10-19
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.0.0, after 3.2.0 (CR-0005) is released"
depends-on: [ "ADR-0020", "ADR-0015", "ADR-0017", "US-0020", "US-0016", "TM-0003", "CR-0005" ]
---

# Change record: Rebalancer 4.0.0 – Redis per-group keys

## Summary

Releases 4.0.0 of all five packages. Redis groups keep their settings / lease and resources in their own keys (`RBR:Lease:{group}`, `RBR:Resources:{group}`), moved automatically on the first 4.0 start (ADR-0020, US-0020).
Breaking for Redis provisioning and rollback; no API change. Release notes: `docs/release-notes/4.0.0.md`; migration
guide: `docs/release-notes/migrating-to-4.0.md`.

## Motivation / drivers

- TM-0003 residual risks: partial per-group isolation and whole-list resource reads (resolved for moved groups).
- ADR-0020's plan: warn in 3.x (CR-0005), switch in 4.0.

## Scope

**In scope:**

- `src/Rebalancer.Redis/`: `Utils/PerGroupKeyMigration.cs` (new), `Utils/RedisHash.cs`, `Leases/LeaseService.cs`,
  `Clients/ClientService.cs`, `Resources/ResourceService.cs`, `RedisProvider.cs` (move on start; 3.2 warning removed).
- Tests: `RedisPerGroupKeysTests` (new); `RedisClientRecordTests` (4.0 layout, ACL user without shared keys);
  `PerGroupKeysDeprecationTests` removed with the warning.
- Versions 4.0.0; release notes; migration guide; US-0020; TM-0003 revision 4 (Proposed).

**Out of scope:**

- SQL Server and ZooKeeper (unchanged apart from the version number).
- The 2.x client hash migration (ADR-0015), unchanged.

## Impact assessment

| Area                  | Impact                                                                                                       |
|-----------------------|--------------------------------------------------------------------------------------------------------------|
| Users / customers     | Redis: provisioning scripts must write the per-group keys; ACL users need the new key patterns.              |
| Services / components | One-time move per group at first start; afterwards only per-group keys are read and written.                 |
| Data / schema         | Redis key layout changes per group (values unchanged). Other groups in the same database are not touched.    |
| Security              | Positive: complete per-group ACL confinement. The first start needs shared-key access once (TM-0003 rev. 4). |
| Operations            | All nodes of a group upgrade at once. Rollback to 3.x needs a manual reverse move (migration guide).         |

## Preconditions

- [x] CR-0005 (3.2.0, with the warning) released first, as ADR-0020 requires (`860b3d3`, 2026-10-19).
- [x] US-0020 readiness approval and TM-0003 revision 4 accepted (dradoaica, 2026-10-19).
- [x] Tests on branch `agent/redis-4.0-per-group-keys` (2026-10-19): see the evidence line below.
- [ ] Rollback procedure validated: the manual reverse move in the migration guide is **not rehearsed**.
- [x] Pre-change test suite passing cleanly.

Approved by dradoaica on 2026-10-19 with one precondition open: the manual rollback to 3.x is not rehearsed.

Evidence (2026-10-19): unit 47/47; SQL Server + Redis integration 16/16 (Docker), including the 3 new
`RedisPerGroupKeysTests`; `Rebalancer.Redis` builds with warnings as errors. ZooKeeper integration not run (no ensemble;
no ZooKeeper code change).

## Implementation plan

1. Release 3.2.0 (CR-0005).
2. Review branch `agent/redis-4.0-per-group-keys` (based on 3.2.0's branch); merge into `main` and push. The push
   publishes 4.0.0; merging is a human action, and the pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" workflow run succeeds, including "Push to GitHub Packages".
- On a staging Redis with a 3.2-provisioned group: the first 4.0 node logs "Moved resource group …", and
  `RBR:Lease:{group}` / `RBR:Resources:{group}` exist.

## Rollback plan

Stop the group's nodes; copy `RBR:Lease:{group}`'s field back into `Dictionary:RBR:ResourceGroups` and the entries of
`RBR:Resources:{group}` into `List:RBR:Resources` (or re-provision there); delete `RBR:Lease:{group}`; start 3.2.0.

## Communications

Release notes `docs/release-notes/4.0.0.md`; migration guide `docs/release-notes/migrating-to-4.0.md` (published with
3.2.0 already).

## Approvals

| Role                   | Name      | Date       |
|------------------------|-----------|------------|
| Maintainer (code)      | dradoaica | 2026-10-19 |
| Release / CAB (or org) | dradoaica | 2026-10-19 |

## Post-implementation

- **Actual outcome (2026-10-19):** `agent/redis-4.0-per-group-keys` merged into `main` as `b9b5f3f` ("Release 4.0.0"),
  merged locally by the agent at dradoaica's request and pushed by dradoaica. The "Release Packages" run on that push
  succeeded at 18:26:25 UTC, including "Push to GitHub Packages" (18:26:23 UTC)
  (https://github.com/dradoaica/Rebalancer/actions/runs/39302402932). Published package versions were not listed from
  the agent session.
- **Deviations:** one precondition was open at approval (recorded under Preconditions): the manual rollback to 3.x is
  not rehearsed. The validation step on a staging Redis is the operators' and is not recorded here.
- **Follow-ups:** rehearse the 4.0 → 3.2 rollback from the migration guide before the first production group moves.
- **Rollback rehearsal (performed after release, 2026-10-19):** released 3.2.0 (`860b3d3`), 4.0.0 (`b9b5f3f`) and 4.1.0 (branch `agent/release-4.1.0`) built side by side and run as single clients against shared containers (Redis
  `redis:7.0`, SQL Server 2022-CU14), lease expiry 5 s, one resource `r1`, a second Redis group left in the shared keys:
  - **Redis:** a 3.2.0-provisioned group moved on the first 4.0.0 start (6.3 s to assignment); the other group stayed
    shared. The reverse move as the guide then described (copy back, delete only `RBR:Lease:{group}`) let 3.2.0 assign
    `r1`, but the next 4.0.0 start **appended** to the leftover `RBR:Resources:{group}` and assigned `[r1,r1]`.
    Corrected: the guide now deletes both per-group keys (re-rehearsed: 4.0.0 → 3.2.0 → 4.0.0 gives `[r1]`), and 4.1.0
    replaces a leftover list (rehearsed with the old steps: `[r1]`; regression test
    `MovingAgainAfterARollback_ReplacesALeftoverResourceList`). Released 4.0.0 keeps the defect; fixed in 4.1.0 (CR-0007).
  - **SQL Server:** 4.0.0 clean stop, then 3.2.0 assigned `r1` after 6.4 s; no data to move back.
- **Note (2026-10-19):** Scope lists "TM-0003 revision 4 (Proposed)", which was its state when this record was
  approved; revision 4 was accepted by dradoaica on 2026-10-19.

## Sources

- ADR: `docs/adrs/0020-per-group-redis-leases-and-resources.md`
- User stories: `docs/user-stories/US-0020-redis-per-group-keys-in-4-0.md`,
  `docs/user-stories/US-0016-warn-redis-groups-about-per-group-keys-in-4-0.md`
- Threat model: `docs/threat-models/TM-0003-redis-coordination.md` (revision 4)
- Previous release: `docs/change-records/CR-0005-release-3-2-0.md`
