# User story: Cross-backend assignment-safety tests

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-013                                             |
| **Status**      | Done                                               |
| **Priority**    | Should                                             |
| **Owner**       |                                                    |
| **SSDLC phase** | Design                                             |

## Story

**As a** maintainer  
**I want** automated tests for SQL Server and Redis backends that assert the same class of invariants as the ZooKeeper suite (no two nodes assigned the same resource concurrently under test conditions)  
**So that** backend parity is validated in CI and regressions are caught early

## Context

Today the ZooKeeper suite under [`tests/Rebalancer.IntegrationTests/ZooKeeper/`](../../tests/Rebalancer.IntegrationTests/ZooKeeper/) provides deep randomized coverage ([US-010](US-010-zookeeper-tests-uphold-assignment-safety.md)). SqlServer and Redis share a structural pattern ([ADR 0002](../adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md)); dedicated Redis/SQL assignment-safety tests live alongside in the same test project.

## Acceptance criteria

1. `tests/Rebalancer.IntegrationTests/` uses **Testcontainers** (Redis + SQL Server) and runs under `dotnet test` when Docker is available.
2. `RedisAssignmentSafetyTests` and `SqlServerAssignmentSafetyTests` sample assignments for two `RebalancerClient` instances sharing one resource and **fail** if both report that resource as assigned at the same observation.
3. `docs/traceability-matrix.md` and root [README.md](../../README.md) document the assembly and Docker requirement.
4. Full randomized parity with the ZooKeeper suite in `tests/Rebalancer.IntegrationTests/ZooKeeper` remains **out of scope** for Redis/SqlServer (see Non-goals and matrix notes).

## Non-goals

- Proving liveness or upper-bound unassignment duration for all failure modes (may remain ZK-only until expanded).
- Multi-machine network partition tests in CI (optional future story).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- Other stories: `docs/user-stories/US-010-zookeeper-tests-uphold-assignment-safety.md`

## Security & compliance

Test databases and Redis instances must use disposable credentials and isolated environments; no production connection strings in repo.

## Definition of done

- [x] Acceptance criteria met
- [x] Tests / checks agreed with team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/`
- `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/`
- `tests/Rebalancer.IntegrationTests/`
