---
id: TM-0004
status: Accepted
date: 2026-11-01
scope: "Rebalancer.PostgreSql provider and the rbr schema it uses"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
depends-on: [ "ADR-0030", "ADR-0026", "ADR-0014", "ADR-0022", "US-0026", "TM-0002" ]
supersedes: [ ]
superseded-by: ""
---

# Threat model: PostgreSQL coordination backend

## Summary

The PostgreSQL backend (ADR-0030) keeps all coordination state in three tables of the `rbr` schema, as the SQL Server
backend does in `RBR` (TM-0002): the lease, the client registrations with their assignments, and the resource list.
Anyone who can write those tables decides which node processes which resource; the library relies entirely on the
PostgreSQL role in the connection string. This model takes over TM-0002's threats where the backend behaves the same,
and adds four that are specific to PostgreSQL: a stalled node holding the group's row lock, object shadowing through
`search_path`, lease writes lost on failover to an asynchronous standby, and connection exhaustion. Written before the
implementation (US-0026); controls marked "to verify" are confirmed by its tests.

## System context and boundaries

- **In scope:** `PostgreSqlProvider` and its `LeaseService`, `ClientService`, `ResourceService`; tables
  `rbr.resource_groups`, `rbr.clients`, `rbr.resources`; the schema and least-privilege role scripts in
  `db/postgresql/` (US-0026, criterion 3). The shared coordination code in `Rebalancer.LeaseCoordination` is the same
  as for SQL Server and Redis (ADR-0029).
- **Out of scope:** PostgreSQL hardening (patching, `pg_hba.conf`, backups, auditing extensions); PostgreSQL-compatible
  stores (CockroachDB, YugabyteDB, Aurora), which need their own models; provisioning tools.
- **Assumptions:**
  - The connection string is supplied by the host application and is a secret.
  - The operator creates the schema and seeds `resource_groups` and `resources`.
  - All nodes of all resource groups that share a database use roles with equal rights.
  - The database is a single primary, or a primary with standbys; nodes always write to the primary.
- **Trust boundaries:**
  1. Application process ↔ PostgreSQL (network, PostgreSQL wire protocol).
  2. Rebalancer nodes ↔ other database roles that can reach the `rbr` schema, or any schema on the role's
     `search_path`.
  3. Resource group ↔ resource group: no boundary inside the schema (see Elevation of Privilege).
  4. Primary ↔ standby: what a standby has not received is lost on failover (see Tampering T3).

## Asset and data classification

| Asset / Data                                                                        | Classification | Notes                                           |
|-------------------------------------------------------------------------------------|----------------|-------------------------------------------------|
| `rbr.clients` (assignments, `coordinator_status`, `fencing_token`)                  | Confidential   | Integrity-critical: decides who processes what  |
| `rbr.resource_groups` (lease: `coordinator_id`, `fencing_token`, expiry, heartbeat) | Confidential   | Integrity-critical: leader election and fencing |
| `rbr.resources` (resource names)                                                    | Internal       | Integrity-critical: defines the work            |
| Connection string (role, password or certificate)                                   | Restricted     | Grants the rights above to anyone who holds it  |
| `coordinator_server` (`Environment.MachineName`)                                    | Internal       | Reveals host names                              |

## Data flows

1. Node → PostgreSQL – opens or reuses a pooled connection per operation (Npgsql pooling), retrying transient errors
   (ADR-0030: SQLSTATE class `08`, `40001`, `40P01`, `57P01`).
2. Node → `SELECT … FOR UPDATE` on `resource_groups`, then `UPDATE`, in one `READ COMMITTED` transaction, time read
   with `clock_timestamp()` after the lock – acquire, renew or relinquish the lease.
3. Node → `INSERT` / `UPDATE` on `clients` – register, keep-alive (store clock), status changes; `DELETE` of its own
   row on a clean stop.
4. Coordinator → `UPDATE` on `clients` – stop or grant resources, only while `resource_groups` names it with its
   fencing token, in the same statement (ADR-0026).
5. Node → `SELECT` on `resources`, `clients` – read resources and live clients.
6. Coordinator → `DELETE` on `clients` – rows of its group whose keep-alive is older than the retention by the store's
   clock, under the same guard, never its own (ADR-0014).
7. Operator / tools → `INSERT` / `DELETE` on `resource_groups`, `resources` – provisioning.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a role with write access inserts a fake client row or takes the lease with a crafted `coordinator_id`.
- Affected flow/asset: flows 2, 3; lease and client registrations.
- Mitigation (s): database permissions only, as on SQL Server (TM-0002). Authenticate nodes with `scram-sha-256` or
  client certificates, not `trust` or `md5`, in `pg_hba.conf` (deployment).
- Risk: **Medium** with a dedicated least-privilege role; **High** if the role is shared with other applications.

### Tampering

- Threat T1: rewrite `clients` assignments or statuses to double-assign or starve resources.
- Mitigation (s): least-privilege role used only by Rebalancer nodes (`db/postgresql/`, US-0026 criterion 3); no other
  role with write access to `rbr`. Fencing tokens protect against stale coordinators, not against a malicious writer.
- Risk: **Medium**.
- Threat T2: SQL injection through group names, resource names or client data.
- Mitigation (s): all values are Npgsql parameters; a list of client ids is passed as one array parameter
  (`= ANY(@ids)`), so no statement text is built from data, unlike the generated placeholder list on SQL Server
  (to verify in review of `ClientService`).
- Risk: **Low**.
- Threat T3 (PostgreSQL-specific): object shadowing. If the node's role resolves unqualified names through a
  `search_path` that includes a schema another role can create objects in (historically `public`), that role can
  shadow a table or function the backend calls.
- Mitigation (s): every statement names `rbr.` explicitly and calls no unqualified function besides built-ins in
  `pg_catalog`; the role script revokes `CREATE` on `public` from the node role (to verify in the scripts and a test
  that runs with a hostile object in `public`).
- Risk: **Low** with qualified names; **Medium** otherwise.
- Threat T4 (PostgreSQL-specific): lease writes lost on failover. With asynchronous streaming replication, a standby
  that is promoted may not have the last committed lease renewal or grant. The old coordinator believes it renewed;
  the new primary shows an older lease that can expire, and another node can take the lease and grant the same
  resources while the old coordinator's nodes still process them.
- Mitigation (s): synchronous replication for the coordination database (`synchronous_commit` with a synchronous
  standby), or a lease expiry longer than the failover time, so a lost renewal cannot expire before the old
  coordinator's next renewal fails against the new primary. The ADR-0026 guard bounds the damage: once another node
  holds the lease, the old coordinator's writes change nothing. Not tested by the suites (single server).
- Risk: **Medium** with asynchronous standbys and short leases; **Low** with synchronous replication or a single
  primary.

### Repudiation

- Threat: an unauthorized change to assignments or the lease cannot be attributed.
- Mitigation (s): none in the library. Use a dedicated role per deployment and database auditing (e.g. statement
  logging or an audit extension) if attribution is required.
- Risk: **Low to Medium**, depending on the deployment's audit requirements.

### Information Disclosure

- Threat I1: credentials in the connection string leak through configuration, source control or logs.
- Mitigation (s): the library does not log connection strings (to verify, as checked for SQL Server); load them from a
  secret store; prefer certificate or cloud identity authentication over passwords where available.
- Risk: **Medium**.
- Threat I2: traffic is read or altered on the network.
- Mitigation (s): TLS with full certificate and host-name verification in the connection string (Npgsql's
  `SSL Mode=VerifyFull`; keyword unverified – please confirm). The test suites may disable verification for local
  containers only.
- Risk: **Medium** without TLS.

### Denial of Service

- Threat D1: unbounded growth of `clients`. Mitigation: the coordinator expires stale rows and a clean stop deletes the
  node's own row (ADR-0014), as on SQL Server. Risk: **Low**.
- Threat D2 (PostgreSQL-specific): a node stalls (process pause, network partition) while its lease transaction holds
  the group's row lock from `SELECT … FOR UPDATE`. Every other node's lease operation on that group waits for the lock,
  so no node can renew or take the lease, and the group stops rebalancing until the stalled transaction ends.
- Mitigation (s): a command timeout below the lease expiry (ADR-0030, US-0026 criterion 8), and for the node role a
  server-side `idle_in_transaction_session_timeout` and `lock_timeout` below the lease expiry, so the server ends a
  stalled transaction and releases the lock (settings to verify; set in the role script). Waiting nodes treat a lock
  timeout as transient and retry.
- Risk: **Medium** until the stall test (US-0026 criterion 8) passes; **Low** after.
- Threat D3: a role with `DELETE` on `rbr.clients` deletes live client rows; each affected node stops and registers
  again (ADR-0014), so repeated deletes keep the group rebalancing. Safety holds. Risk: **Low** with a dedicated role.
- Threat D4 (PostgreSQL-specific): connection exhaustion. Each node holds a connection pool; many nodes, or many
  groups per process, can reach the server's `max_connections` and lock out other applications and the nodes
  themselves.
- Mitigation (s): bound the pool in the connection string (`Maximum Pool Size`; keyword unverified – please confirm),
  give the node role a `CONNECTION LIMIT`, or run a connection pooler; document the sizing in the README.
- Risk: **Low to Medium**, depending on node count.

### Elevation of Privilege

- Threat: all resource groups share one schema, so a node (or its credentials) for group A can modify group B.
- Mitigation (s): none within one database by default. Use separate databases or roles per trust domain. PostgreSQL
  row-level security policies on `resource_group` could confine a role to its groups; not designed for (unverified –
  please confirm before relying on it).
- Risk: **Medium** where groups of different trust share a database; **Low** otherwise.

## Controls mapping

| Threat / Risk                    | Control (design or runtime)                                                                                  | Evidence / Reference                                     |
|----------------------------------|--------------------------------------------------------------------------------------------------------------|----------------------------------------------------------|
| Spoofing / tampering T1, D3      | Dedicated role with only the statements the backend runs; no DDL                                             | `db/postgresql/` role script; least-privilege test       |
| SQL injection T2                 | Parameters only; client-id lists as one array parameter                                                      | `ClientService`, `LeaseService`, `ResourceService`       |
| Object shadowing T3              | Schema-qualified names; `CREATE` on `public` revoked from the node role                                      | Scripts; test with a hostile object in `public`          |
| Lost lease writes on failover T4 | Synchronous replication or lease expiry above failover time; ADR-0026 guard bounds the overlap              | Deployment responsibility; README guidance               |
| Disclosure I1, I2                | Secret store; TLS with full verification; no connection strings in logs                                      | US-0026 Security & compliance; deployment responsibility |
| Row-lock stall D2                | Command timeout, `lock_timeout`, `idle_in_transaction_session_timeout` below the lease expiry               | US-0026 criterion 8 (stall test); role script            |
| Connection exhaustion D4         | Bounded pool; role `CONNECTION LIMIT` or pooler                                                              | README guidance                                          |
| Cross-group tampering            | Separate database or role per trust domain                                                                   | Deployment responsibility                                |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **No authentication of nodes beyond the role,** and no per-group isolation (as TM-0002).
  - **Lost lease writes on failover** with asynchronous standbys (T4): bounded by the ADR-0026 guard, not removed;
    accepted only with the deployment guidance in place.
  - **Telemetry exports group names** (`rebalancer.backend` = `postgresql`, ADR-0022): Low, as for the other backends.
  - **No audit trail** unless the database provides one.
- Decision: accepted by dradoaica (service owner and security / architecture reviewer), in session 2026-11-01.

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0030-postgresql-backend.md`, `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`,
  `docs/adrs/0014-expire-stale-client-records.md` (Accepted).
- Required SDPs: N/A (library).
- Stories implementing the mitigations: `docs/user-stories/US-0026-coordinate-via-postgresql-backend.md`; its
  acceptance criteria should name the T3 test and the server-side timeouts of D2 (added in this revision of the story).
- Runbooks / monitors: the role script in `db/postgresql/`; README guidance on TLS, failover (T4) and pool sizing (D4).

## Approvals

| Role                    | Name      | Date       |
|-------------------------|-----------|------------|
| Service owner           | dradoaica | 2026-11-01 |
| Security / architecture | dradoaica | 2026-11-01 |

## Sources

- ADRs: `docs/adrs/0030-postgresql-backend.md`, `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`,
  `docs/adrs/0014-expire-stale-client-records.md`, `docs/adrs/0022-rebalancing-telemetry-contract.md`
- Related user stories: `docs/user-stories/US-0026-coordinate-via-postgresql-backend.md`
- Related threat model: `docs/threat-models/TM-0002-sql-server-coordination.md` (the threats this model takes over)
- Code: `db/sqlserver/RebalanserTables.sql`, `db/sqlserver/RebalanserLeastPrivilege.sql` (the SQL Server schema and role
  to mirror)
- External standards: STRIDE; OWASP ASVS V5 (validation, injection) and V4 (access control), chapter level as for
  TM-0002
