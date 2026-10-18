# User story: Coordinate via SQL Server backend

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-005                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** platform engineer running on Microsoft SQL Server  
**I want** to use `SqlServerProvider` with my connection string so that coordinator/follower logic and leases persist in the database  
**So that** I can operate Rebalancer alongside existing RDBMS HA and backup practices

## Context

The SQL Server backend mirrors the Redis layout: `SqlServerProvider`, `Coordinator`, `Follower`, `LeaseService`, `ResourceGroupStore`, and client/resource services. See `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`.

## Acceptance criteria

1. `SqlServerProvider` can be constructed with a connection string and optional injected services (`ILeaseService`, `IResourceService`, `IClientService`, `IRebalancerLogger`) per constructor signature in `SqlServerProvider.cs`.
2. The provider implements `IRebalancerProvider` and participates in the same lifecycle as described in US-001 and US-003.
3. The example application registers `SqlServerProvider` against a sample database (`examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`).

## Non-goals

- Supporting every RDBMS dialect beyond what the code targets (e.g., PostgreSQL) without a separate provider.
- Managed cloud-specific HA configuration (always subject to deployment docs).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`, `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

Treat connection strings as secrets; use integrated security or vault-backed credentials per organizational policy. Restrict database permissions to least privilege for the Rebalancer schema only.

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met" and "Docs" ticks verified by an agent against the code and docs; "Tests / checks
agreed with a team" confirmed by dradoaica on 2026-10-18; change-record traceability is ticked once the release ships. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `src/Rebalancer.SqlServer/SqlServerProvider.cs`
- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
- `README.md`
