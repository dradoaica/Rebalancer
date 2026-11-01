---
id: ADR-0030
status: Accepted
date: 2026-11-01
decides: "a PostgreSQL backend for Rebalancer (package, client library, schema, concurrency and clock)"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0005", "ADR-0014", "ADR-0026", "ADR-0029" ]
supersedes: [ ]
superseded-by: ""
---

# A PostgreSQL backend on the shared lease coordination

## Context and Problem Statement

Rebalancer coordinates through SQL Server, Redis or ZooKeeper; the README names PostgreSQL as a store that "would fit
the same pattern but [is] not implemented". Since ADR-0029 a lease-based backend supplies only three services
(`ILeaseService`, `IClientService` with the internal `IFencedClientService`, `IResourceService`) to
`Rebalancer.LeaseCoordination`. Those services must give:

1. a lease per resource group with an increasing integer fencing token, acquired and renewed atomically;
2. lease and keep-alive times by the store's clock, not the nodes' clocks;
3. client-record writes guarded by the lease row in the same atomic step (ADR-0026);
4. deletion of client records whose last keep-alive is older than a retention, by the store's clock (ADR-0014).

This record decides how a PostgreSQL backend gives them: package, client library, schema, concurrency and clock.

## Considered Options

* **Concurrency A. Explicit row lock:** each lease operation runs in a `READ COMMITTED` transaction that first takes
  the group's row with `SELECT … FOR UPDATE`, as the SQL Server backend serializes on its
  `UPDATE … SET LockedByClient`.
* **Concurrency B. `SERIALIZABLE` transactions:** PostgreSQL detects conflicts and aborts one transaction with
  SQLSTATE `40001`, which the backend retries.
* **Clock A. `clock_timestamp()`**, read after the row lock is taken.
* **Clock B. `now()`**, the transaction start time.
* **Client library:** Npgsql, the ADO.NET provider for PostgreSQL (unverified – please confirm the package and the
  version to pin). No other driver is considered: the SQL Server backend is plain ADO.NET, and Npgsql keeps that shape.

## Decision Outcome

Chosen options: "Concurrency A" and "Clock A", with Npgsql, in a new package `Rebalancer.PostgreSql` (name to be
confirmed) with a `PostgreSqlProvider` that delegates to `LeaseCoordinatedProvider` like `SqlServerProvider`.

* **Concurrency A** because it is the SQL Server backend's proven order of operations (lock the group row, read it,
  decide, write), so the lease logic ports statement by statement. B would add a retry path for every operation and
  make contention between nodes an error path; under A, contention only waits.
* **Clock A** because `now()` is fixed when the transaction starts: a node that waits for the row lock would stamp its
  renewal or keep-alive with a time earlier than the write, so other nodes would see the lease or the client expire
  early. `clock_timestamp()` read after the lock matches what `GETUTCDATE()` gives inside the SQL Server `UPDATE`.
* **Schema:** schema `rbr` with tables `resource_groups`, `resources` and `clients`, column for column the `RBR` tables
  of `db/RebalanserTables.sql` (`uuid`, `timestamptz` in UTC, `integer` fencing tokens, `text` for the resources
  list), plus a least-privilege role script that mirrors `db/RebalanserLeastPrivilege.sql`. Creating groups and
  resources stays an operator task, as on SQL Server.
* **Guard (ADR-0026):** `StartActivityAsync` and `StopActivityAsync` update client rows only `WHERE EXISTS` the lease
  row with this coordinator and fencing token, in the same statement; expiry deletion (ADR-0014) uses the same clause.
* **Transient errors:** connection failures (SQLSTATE class `08`), `40001`, `40P01` (deadlock) and `57P01` (admin
  shutdown) count as transient, as `TransientErrorDetector` does for SQL Server error numbers (codes unverified –
  please confirm against the PostgreSQL error-code appendix).
* **Internals:** `Rebalancer.LeaseCoordination` adds `InternalsVisibleTo` for `Rebalancer.PostgreSql`, so the backend
  implements `IFencedClientService` as the SQL Server and Redis backends do.
* **Registration (ADR-0005):** `AddPostgreSqlRebalancer` in `Rebalancer.Extensions.DependencyInjection`, next to the
  SQL Server and Redis methods.
* **Tests:** the shared integration scenarios (assignment safety, randomised, paused node, network fault, assignment
  delay, aborted state, health check, telemetry, least privilege) run against a pinned PostgreSQL container image
  through Testcontainers (module and image tag unverified – please confirm), with the same assertions as for SQL
  Server.
* **Before implementation:** a threat model for the PostgreSQL backend (TM-0004, following TM-0002) and a user story.
  Release in a 5.x minor version: the package is new and changes no existing API.

### Consequences

* Good, because PostgreSQL users get Rebalancer without running SQL Server, Redis or ZooKeeper next to their database.
* Good, because the coordination logic is not touched: the change is three services, a schema and tests (ADR-0029).
* Good, because it is the base for PostgreSQL-compatible stores (CockroachDB, YugabyteDB); each would need its own
  record, as they abort transactions under contention differently.
* Bad, because there is one more backend to maintain, release and test, with its own container in the test run.
* Bad, because `Rebalancer.Extensions.DependencyInjection` then depends on Npgsql, as it already depends on the SQL
  Server, Redis and ZooKeeper client libraries.
* Bad, because a held row lock makes a stalled node block the others' lease operations until its transaction ends;
  the backend sets a command timeout below the lease expiry (the SQL Server backend sets none and uses the driver's
  default).

## Sources

- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md` (registration through the DI extensions)
- `docs/adrs/0014-expire-stale-client-records.md` (expiry of client records by the store's clock)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (Implementation outline, item 1: the guard)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (Decision Outcome: the backend services)
