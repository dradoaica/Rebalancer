---
id: CR-0003
status: Implemented
date: 2026-10-18
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "High"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 3.0.0"
depends-on: [ "ADR-0011", "ADR-0012", "ADR-0013", "ADR-0014", "ADR-0015", "TM-0001", "TM-0002", "TM-0003", "US-0015", "US-005", "US-006", "US-007", "CR-0002" ]
---

# Change record: Rebalancer 3.0.0

## Summary

Releases 3.0.0 of all five packages:

- ADR-0011 and ADR-0013 remove the APIs deprecated in 2.2.0 (CR-0002).
- ZooKeeper entry points require credentials or an explicit ACL.
- Stale client records expire, and nodes re-register safely (ADR-0014).
- Redis client records move to one hash per resource group (ADR-0015).

Release notes: `docs/release-notes/3.0.0.md`; migration guide: `docs/release-notes/migrating-to-3.0.md`.

## Motivation / drivers

- Complete the deprecations announced in 2.2.0 (ADR-0011, ADR-0013).
- TM-0002 / TM-0003 Denial of Service D1: client records grew without bound, and on Redis every node read all of them
  every heartbeat (ADR-0014, ADR-0015).
- TM-0001: ZooKeeper deployments could stay open by accident (ADR-0011).

## Scope

**In scope:**

- `src/Rebalancer.Core`:
  - `Providers` and `RebalancerClient()` removed;
  - `ClientOptions.ClientRecordRetention` added, with `GetClientRecordRetention`.
- `src/Rebalancer.ZooKeeper` and the DI extension:
  - credential-free entry points removed;
  - the ACL entry points validate "credentials or explicit ACL".
- `src/Rebalancer.SqlServer`, `src/Rebalancer.Redis`:
  - `IClientService` gains `DeleteExpiredClientsAsync` and `DeleteClientAsync`, with defaults;
  - coordinator expiry, best effort;
  - re-registration that stops local activity first;
  - a clean stop deletes the node's own record;
  - Redis per-group client hash with a lease-guarded migration of the 2.x entries; the migration is skipped (expiry
    still runs) when the Redis user may not read the 2.x hash, e.g. a per-group ACL user.
- Tests, example, READMEs, release notes, migration guide, US-0015 (successor of US-001), version 3.0.0.

**Out of scope:**

- PRB-0003 (resumed follower window, a 2.x behavior; needs its own decision record).
- Moving Redis leases and resources to per-group keys (ADR-0015 consequences).
- Updates to accepted threat models (see Preconditions).

## Impact assessment

| Area                  | Impact                                                                                                                                                                                          |
|-----------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Breaking: callers of the removed APIs must migrate (guide). ZooKeeper users without credentials must pass `OPEN_ACL_UNSAFE` explicitly. Custom `IClientService` implementations keep compiling. |
| Services / components | Coordinators delete expired client records each heartbeat (best effort; failures only logged). Nodes re-register after their record expired.                                                    |
| Data / schema         | SQL Server: no schema change. Redis: client records move to `RBR:Clients:{group}`; the 2.x hash keeps other groups' entries until those groups upgrade.                                         |
| Security              | SQL login needs `DELETE` on `RBR.Clients` (relaxes TM-0002). ZooKeeper can no longer be open by accident (TM-0001). Redis per-group client keys (TM-0003).                                      |
| Operations            | Redis groups: all nodes at once. SQL Server and ZooKeeper: node by node. Grant `DELETE` before or with the upgrade.                                                                             |

## Preconditions

- [x] Decision records in place: ADR-0011–ADR-0015 (Accepted, `approved-by` set, on the shared trunk).
- [ ] Threat model updates reviewed and accepted: revision 2 of TM-0001, TM-0002 and TM-0003 is drafted (status
  Proposed, `approved-by` cleared) on `release/3.0.0`.
- [ ] Tests / dry-run completed on the release branch: unit tests and Docker integration tests (Redis, SQL Server,
  ZooKeeper single/multi-client/ACL).
- [ ] Rollback procedure validated / dry-run tested (Redis rollback in the migration guide).
- [ ] Pre-change test suite passing cleanly.

## Implementation plan

1. Review branch `release/3.0.0`; merge into `main` and push (publishes 3.0.0; human action).
2. Consumers follow the migration guide: SQL Server grant first; Redis groups stopped and restarted all at once.

## Validation plan

- "Release Packages" workflow run for the merge commit succeeds.
- After a consumer upgrade: every node reaches `Assigned`, no resource reported twice; on Redis, the group's entries
  are gone from `Dictionary:RBR:Clients` and present in `RBR:Clients:{group}`; on SQL Server, records of stopped nodes
  disappear after the retention period.

## Rollback plan

Redeploy 2.2.0 (callers must still compile against 2.2, i.e. use explicit providers; no 3.0-only APIs).

- **Redis:** stop the group and start 2.2; 2.x registers new records in the shared hash, nothing moves back; the
  group's `RBR:Clients:{group}` key may be deleted.
- **SQL Server:** no data change to undo; the `DELETE` grant may stay.

## Communications

Release notes and migration guide; announce the breaking changes and the Redis all-nodes-at-once rule to consumer owners
before publishing.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome (2026-10-18):** `release/3.0.0` merged into `main` as `f3efbcb` ("Release 3.0.0", together with
  the CR-0002 post-implementation commit `4f565cc`) and pushed by dradoaica. The "Release Packages" workflow ran on that
  push and succeeded at 19:02:12 UTC, including "Push to GitHub Packages"
  (https://github.com/dradoaica/Rebalancer/actions/runs/39156837798). Published package versions were not listed from
  the agent session (the GitHub Packages API requires authentication).
- **Test evidence (pre-merge, on the released tree):**
  - unit 39/39;
  - ZooKeeper single-client, multi-client and ACL 22/22 (3-node ensemble), run on `a8b6bb0`; the later commits changed
    no ZooKeeper code;
  - Redis and SQL Server 7/7 (Docker) on `fc2515e`, including the per-group ACL user regression test;
  - build without warnings.
- **Preconditions recorded after approval** (the approved record is immutable, so they are evidenced here):
  - threat model updates: TM-0001, TM-0002 and TM-0003 revision 2 accepted by dradoaica (`e868155`);
  - tests: see above;
  - rollback dry-run: **not performed**.
- **Deviations:** the rollback dry-run was not performed. One defect was found and fixed before release while drafting
  TM-0003: a per-group Redis ACL user blocked the expiry through the migration step (`fc2515e`).
- **Follow-ups:**
  - PRB-0003 (resumed follower window, needs a decision record);
  - a verified minimal Redis ACL command set (TM-0003);
  - per-group keys for Redis leases and resources (TM-0003, needs a decision record);
  - readiness approval of US-0015 (Draft).
- **Rollback rehearsal (performed after release, 2026-10-18):** the released 3.0.0 (`f3efbcb`) and 2.2.0 (`ac6587a`)
  were built side by side and run as single clients against shared containers (Redis `redis:7.0`, SQL Server
  2022-CU14), with lease expiry 5 s and one resource `r1`:
  - **Redis, clean stop:** 3.0.0 assigned `r1` after 7 s, and its clean stop left `RBR:Clients:grp` empty. 2.2.0 then
    assigned `r1` after 7 s and registered in `Dictionary:RBR:Clients`.
  - **Redis, 3.0.0 killed while holding `r1`:** 2.2.0 assigned `r1` after 11 s, after the lease expired. The killed
    node's records stayed in `RBR:Clients:grp`, ignored by 2.2.0.
  - **SQL Server, clean stop:** 3.0.0 assigned `r1` after 7 s and left 0 client rows; 2.2.0 then assigned `r1` after
    7 s.
  - **SQL Server, 3.0.0 killed:** 2.2.0 assigned `r1` after 11 s.

  Result: the rollback plan works as written, and no data needs to move back. The harness was a throwaway console app
  outside the repository.

## Sources

- ADRs: `docs/adrs/0011-…`, `0012-…`, `0013-…`, `0014-expire-stale-client-records.md`,
  `0015-store-redis-clients-per-resource-group.md`
- Threat models: `docs/threat-models/TM-0001-…`, `TM-0002-…`, `TM-0003-…`
- User stories: `docs/user-stories/US-0015-supply-provider-explicitly-to-rebalancer-client.md`, `US-005-…`, `US-006-…`,
  `US-007-…`
- Previous release: `docs/change-records/CR-0002-release-2-2-0-deprecations.md`
