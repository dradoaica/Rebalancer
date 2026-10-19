---
id: US-0020
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19)
priority: "Should"
ssdlc-phase: Coding
depends-on: [ "ADR-0020", "ADR-0015", "ADR-0017", "TM-0003", "US-006", "US-0016" ]
---

# User story: Keep each Redis resource group's lease and resources in its own keys (4.0)

## Story

**As an** operator who runs several Redis-backed resource groups in one Redis database  
**I want** each group's lease and resources in keys of its own  
**So that** a Redis ACL user can be confined to one group, and a group's heartbeat does not read the other groups'
resources

## Context

ADR-0020 (Accepted, option C): 3.x warns (US-0016, 3.2.0); 4.0 switches the layout with an automatic one-time move.

## Acceptance criteria

1. `LeaseService`, `ClientService` and `ResourceService` use `RBR:Lease:{group}` and `RBR:Resources:{group}` only.
2. When a node starts and `RBR:Lease:{group}` does not exist, it moves the group's field from
   `Dictionary:RBR:ResourceGroups` and the group's entries from `List:RBR:Resources` in one guarded transaction; other
   groups' entries stay. Nodes starting together move the group once.
3. A Redis ACL user with access to `RBR:Lease:{g}`, `RBR:Resources:{g}` and `RBR:Clients:{g}` only runs a client end
   to end, on a group provisioned as `docs/release-notes/migrating-to-4.0.md` shows.
4. A user without shared-key access on a group that has not moved gets an error that names the migration guide.
5. The 3.x warning (US-0016) is removed; versions 4.0.0; migration guide, release notes and change record updated.

## Non-goals

- Moving the 2.x client hash (`Dictionary:RBR:Clients`); its migration from ADR-0015 stays as it is.
- A reverse move tool for rollback (documented as manual steps).

## Dependencies

- Decision records: `docs/adrs/0020-per-group-redis-leases-and-resources.md`
- Other stories: `docs/user-stories/US-0016-warn-redis-groups-about-per-group-keys-in-4-0.md`

## Security & compliance

Resolves TM-0003's "partial per-group isolation" for moved groups; the move needs shared-key access once
(TM-0003 revision 4, pending review).

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – `src/Rebalancer.Redis/`, `RedisPerGroupKeysTests` (criteria 2, 4),
  `RedisClientRecordTests` (criterion 3)
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-19)
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) - `docs/change-records/CR-0006-release-4-0-0.md`

## Sources

- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (Decision Outcome, Confirmation)
- `docs/threat-models/TM-0003-redis-coordination.md` (revision 4)
- `docs/release-notes/migrating-to-4.0.md`
