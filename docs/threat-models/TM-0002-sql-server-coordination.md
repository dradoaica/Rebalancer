---
id: TM-0002
status: Accepted
date: 2026-10-18
scope: "Rebalancer.SqlServer provider and the RBR schema it uses"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
depends-on: [ "ADR-0002", "ADR-0014", "US-0005", "CR-0003", "ADR-0022", "ADR-0023", "CR-0007" ]
supersedes: [ ]
superseded-by: ""
---

# Threat model: SQL Server coordination backend

## Summary

All coordination state lives in three tables of the `RBR` schema: the lease, the client registrations with their
assignments, and the resource list. Anyone who can write those tables decides which node processes which resource. The
library has no access control of its own: it relies entirely on the SQL Server login in the connection string. The
statements are parameterized. One query builds an `IN (...)` list, but it concatenates only generated `@ClientN`
placeholder names, never values. The main risks are over-privileged or shared logins, the lack of isolation between
resource groups, and unbounded growth of the client table.

**Revision 2 (3.0.0, CR-0003):**

- Since 3.0 the coordinator deletes stale client records, and a node deletes its own record on a clean stop (ADR-0014). This resolves the unbounded growth (D1).
- The login now needs `DELETE` on `RBR.Clients`, which brings a new availability threat (D3).

**Revision 3 (4.1.0, CR-0007; accepted by dradoaica, 2026-10-19):** telemetry and health checks (ADR-0022, ADR-0023).

- **New data flow out of the process:** metrics and traces carry `rebalancer.backend`, `rebalancer.group` and, on
  the rebalance activity, `rebalancer.fencing_token`. Resource names, node identifiers, connection strings and credentials are never
  emitted (verified by the SQL Server telemetry tests, which fail on a resource name in any tag). Threat: whoever reads
  the telemetry backend learns group names, how often groups rebalance and coordinators change. Risk: **Low**; group
  names are deployment-chosen labels. Mitigation: treat the telemetry backend as Internal data; drop or hash the group
  tag in the collector where group names are sensitive.
- **Health check output** carries only a state and a short reason (no resource names or connection details); it reads
  in-process state and never calls SQL Server.

## System context and boundaries

- **In scope:** `SqlServerProvider`, `LeaseService`, `ClientService`, `ResourceService`, `ConnectionHelper`; tables
  `RBR.ResourceGroups`, `RBR.Clients`, `RBR.Resources` (`db/sqlserver/RebalanserSchema.sql`,
  `db/sqlserver/RebalanserTables.sql`).
- **Out of scope:** SQL Server hardening (patching, TDE, auditing configuration, backups); provisioning tools such as
  `examples/Rebalancer.RabbitMq.Tools` (they write the same tables, with their own credentials).
- **Assumptions:**
  - The connection string is supplied by the host application and is a secret.
  - The operator creates the schema and seeds `ResourceGroups` and `Resources`.
  - All nodes of all resource groups that share a database use logins with equal rights.
- **Trust boundaries:**
  1. Application process ↔ SQL Server (network, TDS).
  2. Rebalancer nodes ↔ other database principals that can reach the `RBR` schema.
  3. Resource group ↔ resource group: no boundary inside the schema (see Elevation of Privilege).

## Asset and data classification

| Asset / Data                                                               | Classification | Notes                                           |
|----------------------------------------------------------------------------|----------------|-------------------------------------------------|
| `RBR.Clients.Resources` (assignments), `CoordinatorStatus`, `FencingToken` | Confidential   | Integrity-critical: decides who processes what  |
| `RBR.ResourceGroups` (lease: `CoordinatorId`, `FencingToken`, expiry)      | Confidential   | Integrity-critical: leader election and fencing |
| `RBR.Resources` (resource names)                                           | Internal       | Integrity-critical: defines the work            |
| Connection string (login, password)                                        | Restricted     | Grants the rights above to anyone who holds it  |
| `CoordinatorServer` (`Environment.MachineName`)                            | Internal       | Reveals host names                              |

## Data flows

1. Node → TDS → SQL Server – opens a connection per operation (`ConnectionHelper`, up to 3 tries on transient errors).
2. Node → `UPDATE … OUTPUT` on `ResourceGroups` (serializable transaction) – acquire or renew the lease.
3. Node → `INSERT` / `UPDATE` on `Clients` – register, keep-alive, status changes; `DELETE` of its own row on a clean
   stop (since 3.0).
4. Coordinator → `UPDATE` on `Clients` (one transaction) – stop or grant resources to followers, guarded by
   `FencingToken`.
5. Node → `SELECT` on `Resources`, `Clients` – read resources and live clients.
6. Coordinator → `DELETE` on `Clients` (since 3.0) – rows of its group with `LastKeepAlive` older than the retention,
   judged by `GETUTCDATE()`, only while `ResourceGroups` shows it as coordinator with its fencing token (ADR-0014).
7. Operator / tools → `INSERT` / `DELETE` on `ResourceGroups`, `Resources` – provisioning.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a principal with write access inserts a fake client row or takes the lease with a crafted `CoordinatorId`.
- Affected flow/asset: flows 2, 3; lease and client registrations.
- Mitigation (s): database permissions only. The library cannot tell a genuine node from any writer with the same rights.
- Risk: **Medium** with a dedicated least-privilege login; **High** if the login is shared with other applications.

### Tampering

- Threat T1: rewrite `Clients.Resources` / `CoordinatorStatus` to double-assign or starve resources.
- Mitigation (s): least-privilege login used only by Rebalancer nodes; no other principals with write access to `RBR`.
  Fencing tokens protect against stale coordinators, not against a malicious writer.
- Risk: **Medium**.
- Threat T2: SQL injection through resource group names or client data.
- Mitigation (s): all values are parameters (`SqlParameter`); the only dynamic SQL concatenates generated placeholder
  names (`ClientService.GetSetStatusQuery`).
- Risk: **Low**.

### Repudiation

- Threat: an unauthorized change to assignments or the lease cannot be attributed.
- Mitigation (s): none in the library. Use SQL Server Audit or temporal tables if attribution is required. Shared logins
  make nodes indistinguishable.
- Risk: **Low to Medium**, depending on the deployment's audit requirements.

### Information Disclosure

- Threat I1: credentials in the connection string leak through configuration, source control, or logs.
- Mitigation (s): the library does not log connection strings (checked 2026-10-18); load them from a secret store; prefer
  integrated or managed-identity authentication over SQL passwords.
- Risk: **Medium**.
- Threat I2: traffic is read on the network.
- Mitigation (s): `Encrypt=True` with certificate validation in the connection string. Note: the test suites and
  examples use `Trust Server Certificate=true`, which is acceptable only for local development.
- Risk: **Medium** without encryption.

### Denial of Service

- Threat D1: client rows are never deleted. Each process start inserts a new `ClientId`, and terminated rows stay,
  so the table grows with every restart.
- Affected flow/asset: flows 3, 5; `GetActiveClientsAsync` filters on status but the table keeps growing.
- Mitigation (s): since 3.0, the coordinator expires rows older than `ClientOptions.ClientRecordRetention` (default
  10 × the keep-alive expiry), and a clean stop deletes the node's own row (ADR-0014). Cleanup is best effort: if it
  fails (e.g. missing `DELETE`), a warning is logged and coordination continues.
- Risk: **Low** (revision 1: Low to Medium).
- Threat D2: lock contention or a held serializable transaction blocks lease renewal, so coordinators flap.
- Mitigation (s): short transactions; lease expiry well above the worst expected blocking.
- Risk: **Low**.
- Threat D3 (revision 2): a principal with `DELETE` on `RBR.Clients` deletes live client rows. Each affected node stops
  its activity (`OnUnassignment`) and registers again (ADR-0014); repeated deletes keep the group rebalancing.
- Mitigation (s): `DELETE` granted on `RBR.Clients` only, to the dedicated Rebalancer login; no other principal with
  write access to `RBR`. Safety holds: a node whose row disappears stops before it registers again.
- Risk: **Low** with a dedicated login; **Medium** if the login is shared.

### Elevation of Privilege

- Threat: all resource groups share one schema, so a node (or its credentials) for group A can modify group B.
- Mitigation (s): none within one database. Use separate databases or logins per trust domain. Row-level security on
  `ResourceGroup` is possible but not designed for (unverified – please confirm before relying on it).
- Risk: **Medium** where groups of different trust share a database; **Low** otherwise.

## Controls mapping

| Threat / Risk                      | Control (design or runtime)                                                                                              | Evidence / Reference                                                                         |
|------------------------------------|--------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------|
| Spoofing / tampering, D3           | Dedicated login with `SELECT`, `INSERT`, `UPDATE` on the three `RBR` tables and `DELETE` on `RBR.Clients` only (no DDL)  | Statements in `src/Rebalancer.SqlServer/`; `docs/release-notes/migrating-to-3.0.md` (grant)  |
| SQL injection                      | Parameterized statements                                                                                                 | `ClientService`, `LeaseService`, `ResourceService`                                           |
| Credential and traffic disclosure  | Secret store; `Encrypt=True`; no connection strings in logs                                                              | US-0005 Security & compliance; deployment responsibility                                     |
| Client table growth (D1)           | Coordinator expiry under the fencing token; own-row delete on clean stop                                                 | ADR-0014; `tests/Rebalancer.IntegrationTests/SqlServer/SqlServerClientRecordTests.cs`        |
| Cross-group tampering              | Separate database or login per trust domain                                                                              | Deployment responsibility                                                                    |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **Telemetry exports group names** (revision 3): accepted as Low; filter in the collector if needed.
  - **No authentication of nodes beyond the login,** and no per-group isolation.
  - ~~**Unbounded `Clients` growth**~~ Resolved in 3.0 (ADR-0014).
  - **Client-row deletes by the login holder** cause rebalancing churn (D3), not double assignment.
  - **No audit trail** unless the database provides one.
- Follow-ups of revision 1: expiry of client rows is decided and implemented (ADR-0014, 3.0.0). The
  least-privilege role script is `db/sqlserver/RebalanserLeastPrivilege.sql` (2026-10-19, verified by
  `SqlServerLeastPrivilegeTests`).
- Decision: revision 1 accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on
  2026-10-18. Revision 2 accepted by dradoaica (in session 2026-10-19; also recorded in
  commit `e868155` of 2026-10-18).

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0014-expire-stale-client-records.md` (Accepted).
- Required SDPs: N/A (library).
- Change records to implement mitigations: `docs/change-records/CR-0003-release-3-0-0.md`.
- Runbooks / monitors: `db/sqlserver/RebalanserLeastPrivilege.sql` (role `rebalancer_node`); the client-table cleanup
  procedure is no longer needed.

## Approvals

| Role                                 | Name      | Date       |
|--------------------------------------|-----------|------------|
| Service owner (revision 1)           | dradoaica | 2026-10-18 |
| Security / architecture (revision 1) | dradoaica | 2026-10-18 |
| Service owner (revision 2)           | dradoaica | 2026-10-19 |
| Security / architecture (revision 2) | dradoaica | 2026-10-19 |
| Service owner (revision 3)           | dradoaica | 2026-10-19 |
| Security / architecture (revision 3) | dradoaica | 2026-10-19 |

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0014-expire-stale-client-records.md`
- Change records: `docs/change-records/CR-0003-release-3-0-0.md`
- Related user stories: `docs/user-stories/US-0005-coordinate-via-sql-server-backend.md`
- Code: `src/Rebalancer.SqlServer/`, `db/sqlserver/RebalanserSchema.sql`, `db/sqlserver/RebalanserTables.sql`
- External standards: STRIDE; OWASP ASVS V5 (validation, injection) and V4 (access control) – control IDs not mapped (not required: decided by dradoaica 2026-10-19; chapter-level references suffice for a library)
