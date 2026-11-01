---
id: US-0026
status: In progress
date: 2026-11-01
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-11-01)
priority: ""
ssdlc-phase: Coding
depends-on: [ "ADR-0030", "ADR-0029", "ADR-0026", "ADR-0014", "ADR-0005", "US-0005", "TM-0004" ]
---

# User story: Coordinate via a PostgreSQL backend

## Story

**As a** team whose services already run on PostgreSQL  
**I want** a `Rebalancer.PostgreSql` package that coordinates resource groups through my PostgreSQL database  
**So that** I get Rebalancer's assignment guarantees without running SQL Server, Redis or ZooKeeper only for it

## Context

ADR-0030 (Accepted) decides the backend: Npgsql, a `rbr` schema mirroring the SQL Server tables, a row lock (`SELECT … FOR UPDATE`) per lease operation, `clock_timestamp()` after the lock as the store clock, the ADR-0026 lease
guard on client writes, and a `PostgreSqlProvider` that delegates to `LeaseCoordinatedProvider` (ADR-0029). This story
is that backend, end to end; its behavior is the SQL Server backend's (US-0005), on PostgreSQL.

**Release (decided by dradoaica, 2026-11-01):** ships in 5.0.0 together with the refactor that creates
`Rebalancer.LeaseCoordination` (ADR-0029), so it is implemented on the release branch `agent/lease-coordination-5.0`
(CR-0013). The threat model TM-0004 is Accepted (2026-11-01).

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
   backend executes, as `db/sqlserver/RebalancerLeastPrivilege.sql` does for SQL Server. The role script also revokes
   `CREATE` on `public` from the node role and sets its `lock_timeout` and `idle_in_transaction_session_timeout` below
   the lease expiry (TM-0004, T3 and D2).
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
8. **Stall:** a node whose lease transaction stalls holding the row lock does not block the others beyond the lease
   expiry: the command timeout and the role's server-side timeouts end the stalled transaction, and the waiting nodes
   retry a lock timeout as transient. A test pauses a node inside a lease transaction and shows the others continue (TM-0004, D2).
9. **Shadowing:** every statement names the `rbr` schema; a test creates a table and a function with the backend's
   names in `public` and shows the backend still uses `rbr` (TM-0004, T3).
10. **Registration:** `AddRebalancerPostgreSql` in `Rebalancer.Extensions.DependencyInjection`, with a test like
    `DiRegistrationTests.AddRebalancerSqlServer_Resolves_RebalancerClient_Without_Static_Providers` (ADR-0005). The name
    follows the existing `AddRebalancerSqlServer` (ADR-0030, corrected 2026-11-01).
11. **Docs:** README (Languages and Backends, quick start, and guidance on TLS with full verification, synchronous
    replication or a lease expiry above the failover time, and pool sizing; TM-0004 I2, T4, D4), architecture overview (backend table and diagram), release notes, and the traceability matrix (ADR-0030 row, this story).
12. The SQL Server, Redis, ZooKeeper and unit tests still pass unchanged.

## Implementation notes for the reviewer (2026-11-01)

Where the implementation differs from the criteria above or from ADR-0030's wording, for confirmation:

1. **Timeouts (criteria 3 and 8):** the lease service sets `lock_timeout` and `idle_in_transaction_session_timeout`
   to half the group's lease expiry in every lease transaction (`set_config(..., true)`), instead of the role script.
   PostgreSQL does not pass `ALTER ROLE ... SET` settings of a group role to its member logins, so the role script
   could not have done it; the stall test shows the bound works.
2. **No foreign key from `rbr.clients` to `rbr.resource_groups`:** with it, a client of a missing group fails inside
   `StartAsync` (SQLSTATE `23503`) instead of reporting `Aborted` (US-0021), and the key check waits for any lease
   transaction holding the group row. `rbr.resources` keeps its key. The shipped SQL Server schema (`db/sqlserver/RebalancerTables.sql`) has the same key, so a real SQL Server install fails the same way; its tests
   pass only because they use their own schema without it. Fixed for both by ADR-0032 (5.0.0).
3. **Transient errors (criterion 6):** besides the codes ADR-0030 names, `55P03` (the waiting nodes' lock timeout) and
   `25P03` (the stalled node's own session ended by the server) are retried, as TM-0004 D2 requires.
4. **Telemetry:** `RebalancerTelemetry.BackendOf` in `Rebalancer.Core` maps `PostgreSqlProvider` to `postgresql`, so
   assignment metrics carry the same backend tag as the coordinator's. ADR-0022 lists the values as `sqlserver`,
   `redis` and `zookeeper`; that list is now incomplete.
5. **Npgsql 8.0.9 (criterion 1):** the last Npgsql line with a `netstandard2.1` build; 9.x and 10.x target net6.0
   and later only. The 8.x line follows .NET 8's support. Moving to a later Npgsql means targeting net8.0 or later
   for this package, unlike the other backends. **Resolved by ADR-0033:** all packages target `net10.0`, which
   replaces criterion 1's `netstandard2.1`, and the backend uses Npgsql 10.0.3.
6. **Recommendation, not done:** `SELECT … FOR NO KEY UPDATE` would serialize lease operations as `FOR UPDATE` does
   without conflicting with key-share locks. Kept `FOR UPDATE`, as ADR-0030 and criterion 4 state.

## Non-goals

- PostgreSQL-compatible stores (CockroachDB, YugabyteDB, Aurora PostgreSQL); each needs its own record (ADR-0030).
- Migrating a group between backends.
- Creating groups and resources from the library; that stays an operator task, as on SQL Server.
- An Entity Framework or Dapper layer; the backend is plain ADO.NET, like SQL Server.

## Dependencies

- Decision records: `docs/adrs/0030-postgresql-backend.md` (Accepted), `docs/adrs/0029-…` (Accepted),
  `docs/adrs/0026-…`, `docs/adrs/0014-…`, `docs/adrs/0005-…`
- Threat model: `docs/threat-models/TM-0004-postgresql-coordination.md` (Accepted)
- Code: the 5.0 refactor on `agent/lease-coordination-5.0` (ADR-0029); this story is built on that branch.
- Other stories: `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md` (the behavior to match)
- Release: 5.0.0 (CR-0013).

## Security & compliance

New network exposure: nodes connect to PostgreSQL with credentials from the connection string. Covered by TM-0004:
credentials and TLS in the connection string, the least-privilege role (criterion 3), who may write the `rbr` schema (a writer can forge leases or assignments, as on SQL Server), the row-lock stall (criterion 8), object shadowing (criterion 9), lost lease writes on failover to an asynchronous standby, and pool sizing (README guidance, criterion
11). No personal data is stored: client ids, group and resource names only.

## Definition of ready

- [x] Governing ADR referenced and `Accepted` on the shared trunk (ADR-0030)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed (TM-0004 `Accepted`)
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
- `docs/threat-models/TM-0004-postgresql-coordination.md` (threats and controls)
- `db/sqlserver/RebalancerTables.sql`, `db/sqlserver/RebalancerLeastPrivilege.sql` (the SQL Server schema to mirror)
