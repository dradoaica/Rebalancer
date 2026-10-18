---
id: TM-0002
status: Accepted
date: 2026-10-18
scope: "Rebalancer.SqlServer provider and the RBR schema it uses"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
depends-on: [ "ADR-0002", "US-005" ]
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

## System context and boundaries

- **In scope:** `SqlServerProvider`, `LeaseService`, `ClientService`, `ResourceService`, `ConnectionHelper`; tables
  `RBR.ResourceGroups`, `RBR.Clients`, `RBR.Resources` (`db/RebalanserSchema.sql`, `db/RebalanserTables.sql`).
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

| Asset / Data                                                       | Classification | Notes                                                         |
|--------------------------------------------------------------------|----------------|---------------------------------------------------------------|
| `RBR.Clients.Resources` (assignments), `CoordinatorStatus`, `FencingToken` | Confidential | Integrity-critical: decides who processes what           |
| `RBR.ResourceGroups` (lease: `CoordinatorId`, `FencingToken`, expiry)       | Confidential | Integrity-critical: leader election and fencing          |
| `RBR.Resources` (resource names)                                   | Internal       | Integrity-critical: defines the work                          |
| Connection string (login, password)                                | Restricted     | Grants the rights above to anyone who holds it                |
| `CoordinatorServer` (`Environment.MachineName`)                    | Internal       | Reveals host names                                            |

## Data flows

1. Node → TDS → SQL Server – opens a connection per operation (`ConnectionHelper`, up to 3 tries on transient errors).
2. Node → `UPDATE … OUTPUT` on `ResourceGroups` (serializable transaction) – acquire or renew the lease.
3. Node → `INSERT` / `UPDATE` on `Clients` – register, keep-alive, status changes.
4. Coordinator → `UPDATE` on `Clients` (one transaction) – stop or grant resources to followers, guarded by
   `FencingToken`.
5. Node → `SELECT` on `Resources`, `Clients` – read resources and live clients.
6. Operator / tools → `INSERT` / `DELETE` on `ResourceGroups`, `Resources` – provisioning.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a principal with write access inserts a fake client row or takes the lease with a crafted `CoordinatorId`.
- Affected flow/asset: flows 2, 3; lease and client registrations.
- Mitigation(s): database permissions only. The library cannot tell a genuine node from any writer with the same rights.
- Risk: **Medium** with a dedicated least-privilege login; **High** if the login is shared with other applications.

### Tampering

- Threat T1: rewrite `Clients.Resources` / `CoordinatorStatus` to double-assign or starve resources.
- Mitigation(s): least-privilege login used only by Rebalancer nodes; no other principals with write access to `RBR`.
  Fencing tokens protect against stale coordinators, not against a malicious writer.
- Risk: **Medium**.
- Threat T2: SQL injection through resource group names or client data.
- Mitigation(s): all values are parameters (`SqlParameter`); the only dynamic SQL concatenates generated placeholder
  names (`ClientService.GetSetStatusQuery`).
- Risk: **Low**.

### Repudiation

- Threat: an unauthorized change to assignments or the lease cannot be attributed.
- Mitigation(s): none in the library. Use SQL Server Audit or temporal tables if attribution is required. Shared logins
  make nodes indistinguishable.
- Risk: **Low to Medium**, depending on the deployment's audit requirements.

### Information Disclosure

- Threat I1: credentials in the connection string leak through configuration, source control, or logs.
- Mitigation(s): the library does not log connection strings (checked 2026-10-18); load them from a secret store; prefer
  integrated or managed-identity authentication over SQL passwords.
- Risk: **Medium**.
- Threat I2: traffic is read on the network.
- Mitigation(s): `Encrypt=True` with certificate validation in the connection string. Note: the test suites and
  examples use `Trust Server Certificate=true`, which is acceptable only for local development.
- Risk: **Medium** without encryption.

### Denial of Service

- Threat D1: client rows are never deleted. Each process start inserts a new `ClientId`, and terminated rows stay,
  so the table grows with every restart.
- Affected flow/asset: flows 3, 5; `GetActiveClientsAsync` filters on status but the table keeps growing.
- Mitigation(s): none in the library today; an operator cleanup job (delete `Terminated` rows older than a threshold).
- Risk: **Low to Medium** (slow degradation, operational).
- Threat D2: lock contention or a held serializable transaction blocks lease renewal, so coordinators flap.
- Mitigation(s): short transactions; lease expiry well above the worst expected blocking.
- Risk: **Low**.

### Elevation of Privilege

- Threat: all resource groups share one schema, so a node (or its credentials) for group A can modify group B.
- Mitigation(s): none within one database. Use separate databases or logins per trust domain. Row-level security on
  `ResourceGroup` is possible but not designed for (unverified – please confirm before relying on it).
- Risk: **Medium** where groups of different trust share a database; **Low** otherwise.

## Controls mapping

| Threat / Risk                         | Control (design or runtime)                                                                  | Evidence / Reference                                     |
|---------------------------------------|----------------------------------------------------------------------------------------------|----------------------------------------------------------|
| Spoofing / tampering                  | Dedicated login with only `SELECT`, `INSERT`, `UPDATE` on the three `RBR` tables (no `DELETE`, no DDL) | Statements in `src/Rebalancer.SqlServer/` (no `DELETE`)  |
| SQL injection                         | Parameterized statements                                                                     | `ClientService`, `LeaseService`, `ResourceService`       |
| Credential and traffic disclosure     | Secret store; `Encrypt=True`; no connection strings in logs                                  | US-005 Security & compliance; deployment responsibility  |
| Client table growth                   | Operator cleanup of old `Terminated` rows                                                    | Not provided; follow-up                                  |
| Cross-group tampering                 | Separate database or login per trust domain                                                  | Deployment responsibility                                |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **No authentication of nodes beyond the login,** and no per-group isolation.
  - **Unbounded `Clients` growth** without an external cleanup.
  - **No audit trail** unless the database provides one.
- Proposed follow-ups (each needs a decision record before implementation): delete or expire terminated client rows
  in the library; document a least-privilege role script next to `db/`.
- Decision: Accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on 2026-10-18,
  including the residual risks above.

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (Accepted).
- Required SDPs: N/A (library).
- Change records to implement mitigations: none yet.
- Runbooks / monitors to add: least-privilege role script; client-table cleanup procedure.

## Approvals

| Role                    | Name | Date |
|-------------------------|------|------|
| Service owner           | dradoaica | 2026-10-18 |
| Security / architecture | dradoaica | 2026-10-18 |

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- Related user stories: `docs/user-stories/US-005-coordinate-via-sql-server-backend.md`
- Code: `src/Rebalancer.SqlServer/`, `db/RebalanserSchema.sql`, `db/RebalanserTables.sql`
- External standards: STRIDE; OWASP ASVS V5 (validation, injection) and V4 (access control) – control IDs not mapped
  (unverified – please confirm if required)
