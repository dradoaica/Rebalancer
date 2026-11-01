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
depends-on: [ "ADR-0029", "ADR-0030", "ADR-0031", "ADR-0032", "ADR-0033", "ADR-0022", "ADR-0026", "TM-0002", "TM-0003", "TM-0004", "US-0025", "US-0026", "CR-0012" ]
---

# Change record: Rebalancer 5.0.0 – shared lease coordination, PostgreSQL backend, Valkey support

## Summary

Releases 5.0.0 of all packages and the new package `Rebalancer.LeaseCoordination`, which holds the coordination code
the SQL Server and Redis backends had as two copies (ADR-0029). The code moves unchanged; the public service types move
to `Rebalancer.LeaseCoordination.*` namespaces, a source and binary break for code that names them, hence a major
version. No behavior, configuration or data format change for SQL Server and Redis. The release also adds the
PostgreSQL backend `Rebalancer.PostgreSql` (ADR-0030, US-0026, TM-0004) and Valkey support for the Redis backend
(ADR-0031, US-0025); the maintainer decided both ship in 5.0.0 (2026-11-01). Release notes:
`docs/release-notes/5.0.0.md`; migration guide: `docs/release-notes/migrating-to-5.0.md`.

## Motivation / drivers

- Every coordination fix had to be made twice (ADR-0026 changed both `Coordinator.cs` files); drift was guarded only by
  discipline and tests (ADR-0002, superseded by ADR-0029).
- A third lease-based backend (PostgreSQL, ADR-0030) supplies only its services against the shared code.
- PostgreSQL users get Rebalancer without running another store; Valkey users get a tested support statement.

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
- Versions 5.0.0 in the eight `.csproj` files.
- New `src/Rebalancer.PostgreSql/` (Npgsql 10.0.3) and `db/postgresql/` (schema, least-privilege role);
  `AddRebalancerPostgreSql`; `InternalsVisibleTo` for it in `Rebalancer.Core` and `Rebalancer.LeaseCoordination`.
- Tests: `tests/Rebalancer.IntegrationTests/PostgreSql/` (the SQL Server suites' counterparts, plus the row-lock stall
  and shadowing tests); `PostgreSqlTransientErrorTests` and DI cases (unit); every Redis integration test as a theory
  over Redis 7.0 and Valkey 8.1.11 (`DockerSupport.RedisServers`).
- Docs: README (backends, supported servers, PostgreSQL operating guidance), architecture overview, release notes,
  traceability matrix; US-0025, US-0026.
- ADR-0032: `db/sqlserver/RebalanserTables.sql` without `FK_Clients_ResourceGroups`; the SQL Server tests run on the
  shipped scripts (`SchemaSetup.ApplyRbrSchemaAsync`), and the test-only `rbr-schema.sql` is deleted. Reproduced
  first: on the shipped scripts with the key, both `SqlServerAbortedStateTests` failed with a foreign-key violation.
- `ProcessingMonitor` (US-0024): resume windows on every resource a node resumes with (see Preconditions).
- `db/`: SQL Server scripts moved to `db/sqlserver/`; `db/postgresql/` has the same three scripts.
- ADR-0033: every `src/` project targets `net10.0` instead of `netstandard2.1`; Npgsql 10.0.3; the
  `System.Diagnostics.DiagnosticSource` reference removed from `Rebalancer.Core` (in the .NET 10 framework, SDK warning
  NU1510). The ADR's premise is confirmed: .NET 8 and .NET 9 reach end of support on November 10, 2026 (Microsoft's
  support policy and .NET blog, checked 2026-11-01). The ADR-0025 Pkcs pin stays: for a `net10.0` host,
  `Microsoft.Data.SqlClient` 7.1.1 resolves its `net9.0` dependency group, which requests Pkcs 9.0.18.
- Patch updates by dradoaica (2026-11-01): the `Microsoft.Extensions.*` packages 10.0.13 → 10.0.14,
  `System.Security.Cryptography.Pkcs` (the ADR-0025 pin) 10.0.13 → 10.0.14, `StackExchange.Redis` 3.4.5 → 3.4.29. No
  package added, removed or major-upgraded. Vulnerability scan after them: none in 13 projects.

**Out of scope:**
- The external NuGet version bumps found in the working tree on 2026-11-01 (stashed; their own change).
- A CI test job (not planned).

**Versioning.** A major release: the packages need .NET 10 (ADR-0033), and public types change namespace
(`.agents/protocols/maintainability.md`, Regression-awareness rules). The strongest argument for a minor release: hosts
that only pass a connection string are not affected. But custom service implementations and test doubles are, and a
minor version must not break them.

## Impact assessment

| Area                  | Impact                                                                                                                                                                                                                                                     |
|-----------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Recompile. Code naming the service types changes its `using` directives (migration guide). A new package dependency, installed by NuGet. New: a PostgreSQL backend; Valkey 8.1.11 supported. Requires .NET 10 (ADR-0033); hosts on .NET 8/9 stay on 4.3.x. |
| Services / components | SQL Server and Redis clients run the same code from one assembly; the store calls, their order and their timing are unchanged.                                                                                                                             |
| Data / schema         | Redis: none. SQL Server: the shipped script drops `FK_Clients_ResourceGroups` (ADR-0032); existing databases keep it until an operator drops it (optional). New for PostgreSQL: the `rbr` schema from `db/postgresql/`.                                    |
| Security              | SQL Server and Redis: none (TM-0002, TM-0003 unchanged; Valkey behaves as Redis in the ACL test). New backend: TM-0004 (Accepted); new third-party dependency Npgsql 10.0.3.                                                                               |
| Operations            | Until the rollback rehearsal below confirms mixed versions, upgrade all nodes of a group at once. PostgreSQL: TLS, replication and pool guidance (README).                                                                                                 |

## Preconditions

- [x] Decision record: ADR-0029 (Accepted by dradoaica, 2026-11-01; on `main`).
- [x] Build: 0 warnings (2026-11-01).
- [x] Unit tests: 103/103 (89 before the new backends).
- [x] Integration tests, all but ZooKeeper (2026-11-01, branch at `6375471`): 101/102. The one failure is
  `RedisNetworkFaultTests` on Redis 7.0, liveness ("never converged"), which passes when run alone; it has failed
  that way in three of four full runs since the Redis suites run on two servers (load; US-0025 notes).
- [x] After ADR-0032 (shipped scripts, no client foreign key): SQL Server suite 18/18 and PostgreSQL suite 20/20.
  Docker here is limited to 7.3 GiB: with xUnit's default parallelism, SQL Server containers fail to start
  (exit codes 17, 114, 183, 235, 255), as in the `main` baseline run; the SQL Server run used
  `-- xUnit.MaxParallelThreads=3`.
- [x] `ProcessingMonitor` fix (US-0024): seed `1306714545`, which failed on `main` (Redis) and on the branch (SQL
  Server, Valkey) before the fix, passes on Valkey and SQL Server after it.
- [x] ZooKeeper integration tests on the README ensemble: **N/A**, accepted by dradoaica (in session 2026-11-01).
  Rationale: no ZooKeeper code changed (`src/Rebalancer.ZooKeeper` differs from `main` only in its version number);
  the `Rebalancer.Core` changes are two `InternalsVisibleTo` entries and a `RebalancerTelemetry.BackendOf` case for
  PostgreSQL, ahead of and not changing the ZooKeeper case; the unit tests cover ZooKeeper's DI registration and
  barrier logic. This deviates from CR-0012, which ran the suite.
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

- The "Release Packages" run succeeds, including "Push to GitHub Packages", and publishes eight packages, among them
  `Rebalancer.LeaseCoordination` and `Rebalancer.PostgreSql` 5.0.0.
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
- ADRs (new backends): `docs/adrs/0030-postgresql-backend.md`,
  `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md`
- ADR (schema fix): `docs/adrs/0032-no-foreign-key-from-client-records-to-resource-groups.md`
- ADR (platform): `docs/adrs/0033-target-net-10-only.md`
- User stories: `docs/user-stories/US-0025-redis-backend-on-valkey.md`,
  `docs/user-stories/US-0026-coordinate-via-postgresql-backend.md`
- Threat models: `docs/threat-models/TM-0002-sql-server-coordination.md`,
  `docs/threat-models/TM-0003-redis-coordination.md` (unchanged),
  `docs/threat-models/TM-0004-postgresql-coordination.md`
- Previous release: `docs/change-records/CR-0012-release-4-3-2.md`
