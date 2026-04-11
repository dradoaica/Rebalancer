# User story: Coordinate via Redis backend

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-006                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

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

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `src/Rebalancer.Redis/RedisProvider.cs`
- `README.md`
