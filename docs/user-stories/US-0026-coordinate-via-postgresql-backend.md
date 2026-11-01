---
id: US-0026
status: Draft
date: 2026-11-01
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: ""
ssdlc-phase: Planning
depends-on: [ "ADR-0030", "ADR-0029", "ADR-0026", "ADR-0014", "ADR-0005", "US-0005" ]
---

# User story: Coordinate via a PostgreSQL backend

## Story

**As a** team whose services already run on PostgreSQL  
**I want** a `Rebalancer.PostgreSql` package that coordinates resource groups through my PostgreSQL database  
**So that** I get Rebalancer's assignment guarantees without running SQL Server, Redis or ZooKeeper only for it

## Context

ADR-0030 (Accepted) decides the backend: Npgsql, a `rbr` schema mirroring the SQL Server tables, a row lock
(`SELECT … FOR UPDATE`) per lease operation, `clock_timestamp()` after the lock as the store clock, the ADR-0026 lease
guard on client writes, and a `PostgreSqlProvider` that delegates to `LeaseCoordinatedProvider` (ADR-0029). This story
is that backend, end to end; its behavior is the SQL Server backend's (US-0005), on PostgreSQL.

**Blocked on:** the threat model of the PostgreSQL backend (TM-0004, to be drafted after TM-0002) must be `Accepted`
before this story can be `Ready` (ADR-0030; Design phase security gate), and the 5.0 refactor that creates
`Rebalancer.LeaseCoordination` (ADR-0029, branch `agent/lease-coordination-5.0`) must be on the shared trunk before
implementation starts.

Tier 1 (new package and client library) under the Accepted ADR-0030; the story carries it. The reviewer confirms or
corrects the tier.

## Acceptance criteria

1. **Package:** `src/Rebalancer.PostgreSql` (netstandard2.1, like the other backends) references
   `Rebalancer.LeaseCoordination` and Npgsql pinned to one version; the packed nuspec lists both. It is in
   `Rebalancer.slnx` and packed by `release.yml` without a workflow change.
2. **Provider:** `PostgreSqlProvider(string connectionString, IRebalancerLogger logger = null, ILeaseService
   leaseService = null, IResourceService resourceService = null, IClientService clientService = null)`, with the same
   public members as `SqlServerProvider`, delegating to `LeaseCoordinatedProvider` with backend tag `"postgresql"`.
3. **Schema scripts:** `db/postgresql/` holds the schema and tables (`rbr.resource_groups`, `rbr.resources`,
   `rbr.clients`, column for column the `RBR` tables) and a least-privilege role script granting exactly what the
   backend executes, as `db/RebalanserLeastPrivilege.sql` does for SQL Server.
4. **Lease (ADR-0030):** acquire, renew and relinquish take the group row with `SELECT … FOR UPDATE` in one
   `READ COMMITTED` transaction and read the time with `clock_timestamp()` after the lock; `now()` is not used for any
   lease or keep-alive time.
5. **Guard and expiry:** `StartActivityAsync` / `StopActivityAsync` change client rows only while the lease row names
   the coordinator and fencing token, in the same statement (ADR-0026); expired client rows are deleted by the store's
   clock under the same guard, never the coordinator's own (ADR-0014). The backend implements `IFencedClientService`
   (`InternalsVisibleTo` in `Rebalancer.LeaseCoordination`).
6. **Transient errors:** SQLSTATE class `08`, `40001`, `40P01` and `57P01` are retried like SQL Server's transient
   errors; any other error fails the operation. Unit tests cover the classification.
7. **Integration tests:** the SQL Server suites have PostgreSQL counterparts with the same assertions (assignment
   safety, randomised, paused node, network fault, assignment delay, aborted state, client records, least privilege,
   health check, telemetry), against a PostgreSQL container image pinned in `DockerSupport.cs`.
8. **Stall:** a node whose lease transaction stalls holding the row lock does not block the others beyond the command
   timeout, which is set below the lease expiry; a test pauses a node inside a lease transaction and shows the others
   continue.
9. **Registration:** `AddRebalancerPostgreSql` in `Rebalancer.Extensions.DependencyInjection`, with a test like
   `DiRegistrationTests.AddRebalancerSqlServer_Resolves_RebalancerClient_Without_Static_Providers` (ADR-0005). The name
   follows the existing `AddRebalancerSqlServer`; ADR-0030 writes `AddPostgreSqlRebalancer`, which does not match the
   convention (the reviewer confirms the name).
10. **Docs:** README (Languages and Backends, quick start), architecture overview (backend table and diagram), release
    notes, and the traceability matrix (ADR-0030 row, this story).
11. The SQL Server, Redis, ZooKeeper and unit tests still pass unchanged.

## Non-goals

- PostgreSQL-compatible stores (CockroachDB, YugabyteDB, Aurora PostgreSQL); each needs its own record (ADR-0030).
- Migrating a group between backends.
- Creating groups and resources from the library; that stays an operator task, as on SQL Server.
- An Entity Framework or Dapper layer; the backend is plain ADO.NET, like SQL Server.

## Dependencies

- Decision records: `docs/adrs/0030-postgresql-backend.md` (Accepted), `docs/adrs/0029-…` (Accepted),
  `docs/adrs/0026-…`, `docs/adrs/0014-…`, `docs/adrs/0005-…`
- Threat model: TM-0004 for the PostgreSQL backend – not drafted yet; blocks readiness.
- Code: the 5.0 refactor on `agent/lease-coordination-5.0` – not on the shared trunk yet; blocks implementation.
- Other stories: `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md` (the behavior to match)
- Release: a 5.x minor version with its change record.

## Security & compliance

New network exposure: nodes connect to PostgreSQL with credentials from the connection string. Covered by TM-0004
(to be drafted): credentials and TLS in the connection string, the least-privilege role (criterion 3), who may write
the `rbr` schema (a writer can forge leases or assignments, as on SQL Server, TM-0002), and the row-lock stall
(criterion 8). No personal data is stored: client ids, group and resource names only.

## Definition of ready

- [x] Governing ADR referenced and `Accepted` on the shared trunk (ADR-0030)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [ ] Security & compliance considerations addressed (TM-0004 `Accepted`)
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/adrs/0030-postgresql-backend.md` (Decision Outcome)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (the services a backend supplies)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (the guard)
- `docs/adrs/0014-expire-stale-client-records.md` (expiry by the store's clock)
- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md` (registration)
- `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md`
- `db/RebalanserTables.sql`, `db/RebalanserLeastPrivilege.sql` (the SQL Server schema to mirror)
