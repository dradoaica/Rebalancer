---
id: US-006
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Must"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0001", "ADR-0002", "US-005" ]
---

# User story: Coordinate via Redis backend

## Story

**As a** platform engineer using Redis  
**I want** to use `RedisProvider` with a StackExchange.Redis connection string  
**So that** coordination is fast and can leverage existing Redis clusters for meta-data and leases

## Context

`RedisProvider` connects via `ConnectionMultiplexer`, uses `ResourceGroupStore`, lease and client/resource services, and coordinator/follower roles analogous to SQL Server. See ADR 0002.

## Acceptance criteria

1. `RedisProvider` constructor accepts a Redis connection string and optional `IRebalancerLogger` and injectable `ILeaseService`, `IResourceService`, `IClientService` consistent with `RedisProvider.cs`.
2. The provider implements `IRebalancerProvider` end-to-end (start, completion wait, assigned resources, state).
3. Story references document parallel structure with `src/Rebalancer.SqlServer/` (under `src/`) for behavioral expectations where algorithms align.

## Non-goals

- Redis Cluster slot migration policies beyond what the library assumes.
- TLS/certificate management (deployment concern).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`, `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-005-coordinate-via-sql-server-backend.md` (conceptual sibling)

## Security & compliance

Protect Redis with ACL/password/TLS as required; connection strings are secrets. Prefer private network paths between app tier and Redis.

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met" and "Docs" ticks verified by an agent against the code and docs; "Tests / checks
agreed with a team" confirmed by dradoaica on 2026-10-18; change-record traceability ticked after the 2.1.0 release. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) — `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md` (2.1.0), `docs/change-records/CR-0003-release-3-0-0.md` (3.0.0)

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `src/Rebalancer.Redis/RedisProvider.cs`
- `README.md`
