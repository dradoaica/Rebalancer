---
id: CR-0013
status: Draft
date: 2026-11-01
owner: "dradoaica"
approved-by: [ ]            # set by a human only
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 5.0.0, after approval"
depends-on: [ "ADR-0029", "ADR-0022", "ADR-0026", "TM-0002", "TM-0003", "CR-0012" ]
---

# Change record: Rebalancer 5.0.0 – shared lease coordination for SQL Server and Redis

## Summary

Releases 5.0.0 of all packages and the new package `Rebalancer.LeaseCoordination`, which holds the coordination code
the SQL Server and Redis backends had as two copies (ADR-0029). The code moves unchanged; the public service types move
to `Rebalancer.LeaseCoordination.*` namespaces, a source and binary break for code that names them, hence a major
version. No behavior, configuration or data format change. Release notes: `docs/release-notes/5.0.0.md`; migration
guide: `docs/release-notes/migrating-to-5.0.md`.

## Motivation / drivers

- Every coordination fix had to be made twice (ADR-0026 changed both `Coordinator.cs` files); drift was guarded only by
  discipline and tests (ADR-0002, superseded by ADR-0029).
- A third lease-based backend (PostgreSQL, ADR-0030) supplies only its services against the shared code.

## Scope

**In scope:**

- New `src/Rebalancer.LeaseCoordination/`: roles, store, service interfaces and models moved from the backends, and
  the internal `LeaseCoordinatedProvider` (the provider loop, derived from `SqlServerProvider`).
- `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/`: providers delegate to `LeaseCoordinatedProvider`; services
  import the moved types; the Redis copies of the moved files are deleted.
- `src/Rebalancer.Core/Rebalancer.Core.csproj` (`InternalsVisibleTo` for the new project),
  `src/Rebalancer.Extensions.DependencyInjection/` (namespaces), `Rebalancer.slnx`.
- Tests: namespaces, qualifiers and the `Coordinator` backend argument only; no assertion changed.
- Docs: release notes, migration guide, README, architecture overview, product baseline, traceability matrix, Sources
  paths in PRB-0001 / PRB-0005 / PRB-0007.
- Versions 5.0.0 in the seven `.csproj` files.

**Out of scope:**

- The PostgreSQL backend (ADR-0030, US-0026) and Valkey support (ADR-0031, US-0025); later 5.x releases.
- The external NuGet version bumps found in the working tree on 2026-11-01 (stashed; their own change).
- A CI test job (not planned).

**Versioning.** A major release: public types change namespace (`.agents/protocols/maintainability.md`,
Regression-awareness rules). The strongest argument for a minor release: hosts that only pass a connection string are
not affected. But custom service implementations and test doubles are, and a minor version must not break them.

## Impact assessment

| Area                  | Impact                                                                                                                                   |
|-----------------------|------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Recompile. Code naming the service types changes its `using` directives (migration guide). A new package dependency, installed by NuGet. |
| Services / components | SQL Server and Redis clients run the same code from one assembly; the store calls, their order and their timing are unchanged.           |
| Data / schema         | None. SQL Server schema and Redis keys unchanged.                                                                                        |
| Security              | None to the threat models' assets or trust boundaries (TM-0002, TM-0003 unchanged); no new dependency on a third-party package.          |
| Operations            | Until the rollback rehearsal below confirms mixed versions, upgrade all nodes of a group at once.                                        |

## Preconditions

- [x] Decision record: ADR-0029 (Accepted by dradoaica, 2026-11-01; on `main`).
- [x] Build: 0 warnings (2026-11-01).
- [x] Unit tests: 89/89, as on `main` before the change.
- [x] SQL Server + Redis integration tests: 54/54 (commit `f4ad1c4`; the rebase onto `main` changed only
  `docs/traceability-matrix.md`). Baseline on `main` the same day: 51/54, the three failures SQL Server containers out
  of memory (`FAIL_PAGE_ALLOCATION`), not test failures.
- [ ] ZooKeeper integration tests on the README ensemble: not run. ZooKeeper code is unchanged; `Rebalancer.Core`'s
  project file gained one `InternalsVisibleTo`.
- [x] Dependency vulnerability scan (`dotnet list Rebalancer.slnx package --vulnerable --include-transitive`), as since
  CR-0009: no vulnerable package in any of the 12 projects (2026-11-01, nuget.org). No third-party dependency changed.
- [x] Packages: the packed `Rebalancer.SqlServer` and `Rebalancer.Redis` nuspecs list `Rebalancer.LeaseCoordination`.
- [ ] Rollback procedure validated: 5.0.0 → 4.3.2, and whether one group can run 4.3.2 and 5.0.0 nodes together (the
  protocol and data are unchanged, so it is expected; not yet shown). To run before approval, as for CR-0012.

## Implementation plan

1. Versions 5.0.0 and "current version" in the docs: done on branch `agent/lease-coordination-5.0`.
2. Run the open preconditions above; record the results here.
3. Merge the branch into `main` (human) and push (human). The push publishes 5.0.0; the pushed head commit must not
   carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages", and publishes seven packages, among them
  `Rebalancer.LeaseCoordination` 5.0.0.
- A host built against the published 5.0.0 `Rebalancer.SqlServer` restores `Rebalancer.LeaseCoordination` without a
  direct reference.

## Rollback plan

Redeploy 4.3.2 and revert the host's `using` changes; no data or configuration to undo. `Rebalancer.LeaseCoordination`
is then unused and can be removed from the host's dependencies.

## Communications

Release notes `docs/release-notes/5.0.0.md` and the migration guide `docs/release-notes/migrating-to-5.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome:** {after change}
- **Follow-ups:** {tickets, problem records if needed}

## Sources

- ADRs: `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (Decision Outcome, Consequences),
  `docs/adrs/0022-rebalancing-telemetry-contract.md` (backend tag),
  `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`
- Threat models: `docs/threat-models/TM-0002-sql-server-coordination.md`,
  `docs/threat-models/TM-0003-redis-coordination.md` (unchanged)
- Previous release: `docs/change-records/CR-0012-release-4-3-2.md`
