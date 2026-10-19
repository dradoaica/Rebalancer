---
id: US-0016
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19)
priority: "Should"
ssdlc-phase: Coding
depends-on: [ "ADR-0020", "ADR-0015", "TM-0003", "US-006" ]
---

# User story: Warn Redis operators that 4.0 moves leases and resources to per-group keys

## Story

**As an** operator of a Redis-backed resource group  
**I want** a warning from the library and a documented provisioning procedure before 4.0 changes the Redis key layout  
**So that** I can update my provisioning scripts and Redis ACL users before upgrading, instead of finding out from a
broken deployment

## Context

ADR-0020 (Accepted, option C) moves Redis leases from the shared `Dictionary:RBR:ResourceGroups` hash and resources
from the shared `List:RBR:Resources` list to per-group keys (`RBR:Lease:{group}`, `RBR:Resources:{group}`) in 4.0.
It requires a 3.x minor first that documents per-group provisioning and logs a warning while a group is still
provisioned in the shared keys, following the deprecate-then-remove pattern of ADR-0011 and ADR-0013.

**Open question, decided 2026-10-19.** A 3.x node reads only the shared keys (ADR-0020 rejected a dual layout,
option A), so the warning fires for every Redis group on every start. The maintainer delegated the choice to the
smallest-complete rule: keep the Warning as ADR-0020 states it, with no opt-out (an opt-out would be new public API).

## Acceptance criteria

1. When a `RedisProvider` client starts for a resource group, it logs exactly one warning per client session (per
   `StartAsync`, not per heartbeat) through its `IRebalancerLogger`. The warning names the group, says that 4.0 moves
   its lease and resources to `RBR:Lease:{group}` and `RBR:Resources:{group}`, and links the provisioning section of
   the migration guide.
2. A restart after an error (`AutoRecoveryOnError`) within the same session does not log it again.
3. Behavior is otherwise unchanged: no key is read or written that 3.1.0 does not read or write; the minimal per-group
   Redis ACL user from `docs/release-notes/migrating-to-3.0.md` keeps working (existing `RedisClientRecordTests`).
4. A "Preparing for 4.0 (Redis)" section, in the 3.x release notes or a new `docs/release-notes/migrating-to-4.0.md`,
   documents: the new key names and how the group name is escaped (as in ADR-0015); the one-time automatic move on the
   first 4.0 start; that all nodes of a group upgrade together; the per-group Redis ACL user for 4.0 (no access to the
   shared keys); and the rollback limitation (a reverse move or re-provisioning in the shared keys).
5. A unit test covers criteria 1 and 2 with a recording logger: one warning per session, none on recovery restarts,
   and none for SQL Server or ZooKeeper providers.

## Non-goals

- The 4.0 key layout and migration themselves (a separate story and change record, after this one ships).
- An option to silence the warning (see the open question; needs a decision record if wanted).
- Moving the resource-group definitions (`Dictionary:RBR:ResourceGroups` fields other than the lease) – not covered
  by ADR-0020.

## Dependencies

- Decision records: `docs/adrs/0020-per-group-redis-leases-and-resources.md` (Accepted; Decision Outcome, Confirmation),
  `docs/adrs/0015-store-redis-clients-per-resource-group.md` (key escaping)
- Threat model: `docs/threat-models/TM-0003-redis-coordination.md` (residual risk "partial per-group isolation")
- Other stories: `docs/user-stories/US-006-coordinate-via-redis-backend.md`
- Release: a 3.x minor with its own change record.

## Security & compliance

No change to data or access in 3.x. The documentation in criterion 4 is the first half of resolving TM-0003's
"partial per-group isolation" residual risk; the warning must not log connection strings or credentials.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified
- [x] Open question (warning on every start) decided (2026-10-19, see Context)

## Definition of done

- [x] Acceptance criteria met – `src/Rebalancer.Redis/RedisProvider.cs` (`StartAsync`), `docs/release-notes/migrating-to-4.0.md`,
  `tests/Rebalancer.UnitTests/PerGroupKeysDeprecationTests.cs` (criterion 5: Redis and SQL Server; ZooKeeper has no
  code path to the warning, checked by inspection)
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-19)
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) - `docs/change-records/CR-0005-release-3-2-0.md`

## Sources

- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (Considered Options C, Decision Outcome, Confirmation)
- `docs/adrs/0015-store-redis-clients-per-resource-group.md`
- `docs/threat-models/TM-0003-redis-coordination.md` (Residual risk and acceptance)
- `docs/user-stories/US-006-coordinate-via-redis-backend.md`
- Code: `src/Rebalancer.Redis/RedisProvider.cs`, `src/Rebalancer.Redis/Resources/ResourceService.cs`,
  `src/Rebalancer.Redis/Leases/LeaseService.cs`
