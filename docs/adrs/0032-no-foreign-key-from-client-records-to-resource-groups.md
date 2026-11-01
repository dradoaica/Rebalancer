---
id: ADR-0032
status: Accepted
date: 2026-11-01
decides: "referential constraints of the client records table (RBR.Clients, rbr.clients) in the shipped schemas"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0014", "ADR-0029", "ADR-0030", "US-0021" ]
supersedes: [ ]
superseded-by: ""
---

# No foreign key from client records to resource groups

## Context and Problem Statement

The shipped SQL Server schema (`db/sqlserver/RebalanserTables.sql`) has a foreign key `FK_Clients_ResourceGroups`
from `RBR.Clients` to `RBR.ResourceGroups`. A client registers (`INSERT` into `RBR.Clients`) in `StartAsync`, before
its first lease request. For a group that does not exist, that insert fails with a foreign-key violation, so
`StartAsync` throws, instead of the client running, getting `NoLease` from its lease request, and reporting `Aborted`
(without auto-recovery) as US-0021 requires. Seen on PostgreSQL with the same key; on SQL Server it is reproduced as the
first implementation step, by running the tests on the shipped scripts before the key is dropped.

The integration tests did not see it: all SQL Server tests except the least-privilege one create their schema from a
test-only copy (`tests/Rebalancer.IntegrationTests/Sql/rbr-schema.sql`) without foreign keys. The PostgreSQL backend
met the same failure with the same key and dropped it (US-0026, implementation notes, item 2). This record decides the
constraint for both shipped schemas, and makes the tests use the shipped scripts.

## Considered Options

* **A. Drop the foreign key from the client records table** in both shipped schemas; keep the one on resources.
* **B. Keep it, and check the group before registering:** the provider reads the lease row before its insert and
  reports a missing group as an error there. A change to `LeaseCoordinatedProvider` and every backend's start path.
* **C. Keep it, and change US-0021:** a client of a missing group fails in `StartAsync`, as it does today.

## Decision Outcome

Chosen option: "A. Drop the foreign key", because the client loop already detects a missing group (`NoLease`) and
reports it as US-0021 requires; the key only moves that failure to an earlier point, where it bypasses the error
handling and the `Aborted` state. B adds code to restore what the key took away; C breaks an accepted story.

* `db/sqlserver/RebalanserTables.sql` no longer creates `FK_Clients_ResourceGroups`; `FK_Resources_ResourceGroups`
  stays (resources are operator data). `db/postgresql/RebalanserTables.sql` already has no such key.
* Existing SQL Server databases keep the key until an operator drops it:
  `ALTER TABLE RBR.Clients DROP CONSTRAINT FK_Clients_ResourceGroups;` (5.0 migration guide). Nothing else depends on
  it; without the drop, behavior stays as in 4.x.
* The SQL Server tests create their schema from the shipped scripts, as the PostgreSQL tests do, and the test-only
  `rbr-schema.sql` is deleted, so the tests check what operators install.
* Release: 5.0.0 (CR-0013).

### Consequences

* Good, because a client of a missing group reports `Aborted` (or keeps retrying with auto-recovery), on both backends.
* Good, because the tests run the shipped schema, so a difference like this one cannot hide in a test-only copy again.
* Bad, because a client of a missing group leaves a record that no coordinator expires (ADR-0014 expiry runs under the
  group's lease): one per start attempt with a new client id, removed on a clean stop, or by an operator, or by the
  first coordinator once the group is created.
* Bad, because existing SQL Server databases need a manual step to get the fix.

## Sources

- `docs/adrs/0014-expire-stale-client-records.md` (expiry runs under the group's lease)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (the shared client loop)
- `docs/adrs/0030-postgresql-backend.md` (the PostgreSQL schema)
- `docs/user-stories/US-0021-report-aborted-state-on-sql-server-and-redis.md` (the Aborted state required)
