# Security Policy

Rebalancer is a set of .NET libraries that coordinate resource assignment through a backing store (SQL Server, Redis
or ZooKeeper). The store is the trust anchor: whoever can write to it can change which node owns which resource. Most
security questions about Rebalancer are therefore questions about how a backend is used and protected.

## Supported versions

Only the latest release receives security fixes. Upgrade within the current major version before reporting an issue
against an older one.

| Version | Supported                                    |
|---------|----------------------------------------------|
| 4.2.x   | Yes (4.2.1 or later)                         |
| < 4.2   | No – upgrade; see the migration guides below |

All packages (`Rebalancer.Core`, `Rebalancer.SqlServer`, `Rebalancer.Redis`, `Rebalancer.ZooKeeper`,
`Rebalancer.Extensions.DependencyInjection`, `Rebalancer.Extensions.Diagnostics.HealthChecks`) share one version
number; run them at the same version on every node of a resource group.

If you cannot upgrade, say why in your report so we understand your situation. Upgrade notes:
[3.0](docs/release-notes/migrating-to-3.0.md), [4.0](docs/release-notes/migrating-to-4.0.md).

### Known advisories

| Advisory                                                                                                                                    | Affects                                                                        | Fixed in                             |
|---------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------|--------------------------------------|
| CVE-2026-84008 ([GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx)) in transitive `System.Security.Cryptography.Pkcs` | `Rebalancer.SqlServer` and `Rebalancer.Extensions.DependencyInjection` ≤ 4.2.0 | [4.2.1](docs/release-notes/4.2.1.md) |

## Reporting a vulnerability

**Do not open a public issue for a vulnerability.** Report it privately through GitHub:
[Report a vulnerability](https://github.com/dradoaica/Rebalancer/security/advisories/new) (Security tab → Advisories).

Please include:

- the affected package (s) and version, and the backend (SQL Server, Redis, ZooKeeper);
- the .NET runtime and backend server version;
- what an attacker needs (network access to the store, a store login, code running in a node, …) and what they gain;
- steps or a minimal test to reproduce it (the scenarios in `tests/Rebalancer.IntegrationTests/` are a good base).

This is a project maintained in spare time, so response times are best effort. We aim to acknowledge a report within
7 days and to agree a disclosure date with you once the issue is confirmed. Fixes ship as a patch release with release
notes, a problem record in `docs/problem-records/` and, where it applies, a GitHub security advisory.

Non-sensitive security questions and hardening suggestions are welcome as regular
[issues](https://github.com/dradoaica/Rebalancer/issues).

## Scope

In scope – the code in `src/` and the SQL scripts in `db/`, for example:

- a node gaining, keeping or losing a resource in a way the protocol does not allow (two nodes holding the same
  resource, a demoted coordinator or paused follower still acting on resources);
- a client of the store that, with only the rights the documentation says it needs, can take over or disrupt another
  resource group;
- credentials, connection strings or resource names leaking into logs, metrics, traces or health-check output;
- vulnerable dependencies shipped by the packages.

Out of scope:

- hardening of the backend servers themselves (TLS, authentication, patching, network exposure of SQL Server, Redis or
  ZooKeeper) – this is the operator's responsibility;
- attacks that require already being able to write to the store with full rights, or to run code inside a node;
- the samples in `examples/`: they are not published as packages and use development defaults (still, tell us if a
  sample teaches an unsafe practice);
- the resources Rebalancer assigns (queues, buckets, file shares) and their access control.

## Threat models and hardening

Each backend has an accepted threat model that lists its trust boundaries, assumptions and residual risks. Read the one
for your backend before deploying:

- [TM-0001 ZooKeeper coordination and znode ACLs](docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md)
- [TM-0002 SQL Server coordination](docs/threat-models/TM-0002-sql-server-coordination.md)
- [TM-0003 Redis coordination](docs/threat-models/TM-0003-redis-coordination.md)

In short:

- **Treat the connection string or ZooKeeper credentials as a secret.** They grant control over assignment.
- **SQL Server:** run nodes with the least-privilege role from [
  `db/sqlserver/RebalanserLeastPrivilege.sql`](db/sqlserver/RebalanserLeastPrivilege.sql)
  rather than a schema owner. All groups in one database share the `RBR` schema, so isolate groups that must not
  influence each other in separate databases.
- **Redis:** Rebalancer has no access control of its own. Use a dedicated ACL user limited to the Rebalancer keys and
  `ssl=true` with certificate validation; without a password, anyone who reaches the port controls assignment.
- **ZooKeeper:** configure ACLs (`authScheme` / `authData`) and encrypt client connections when using the digest scheme,
  whose credentials otherwise travel in clear text. Rebalancer warns when operator-created znodes are left open.
- Metrics and traces carry the backend, group name and coordinator epoch; health checks report a state and a short
  reason. Resource names, node identifiers, connection strings and credentials are never emitted. Group names may still
  be sensitive to you – consider that before exporting telemetry to a third party.
