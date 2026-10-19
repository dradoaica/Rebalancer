---
id: US-0019
status: Draft
date: 2026-10-19
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Should"
ssdlc-phase: Planning
depends-on: [ "ADR-0002", "US-010", "US-013" ]
---

# User story: Randomised assignment-safety tests for SQL Server and Redis

## Story

**As a** maintainer  
**I want** randomised multi-node tests for the SQL Server and Redis backends, like the ZooKeeper suite  
**So that** races in the coordinator / follower protocol show up in tests rather than in production

## Context

US-013 added sampled two-node safety tests and left "full randomized parity with the ZooKeeper suite" out of scope.
The product baseline lists it as backlog. Two defects in this protocol were found by review rather than by tests
(PRB-0001, PRB-0003); `PausedNodeScenarios` (2026-10-19) covers those two scenarios only.

## Acceptance criteria

1. A randomised test per backend (Docker) runs N clients (default 5) on M resources (default 10) and, for a
   configurable duration, randomly starts, stops, kills (no clean stop) and pauses (`NodePause`) clients.
2. A resource monitor records every assignment and unassignment and fails on a resource held by two clients at the
   same time, outside the documented window of a resumed paused client (ADR-0019, ADR-0021).
3. It fails if a resource stays unassigned longer than a bound derived from the group's lease expiry and heartbeat,
   while at least one client is running and not paused.
4. The random seed is logged, and a failing run can be replayed with that seed.
5. Duration is capped by `REBALANCER_RANDOMISED_TEST_MAX_MINUTES`, as for the ZooKeeper suite (US-010).

## Non-goals

- Network partitions between a node and the store (needs a proxy, a new dependency).
- Running the suite in CI (see the CI test job, not planned yet).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`,
  `docs/adrs/0021-assignment-generation-for-resource-handlers.md`
- Other stories: `docs/user-stories/US-010-zookeeper-tests-uphold-assignment-safety.md`,
  `docs/user-stories/US-013-cross-backend-assignment-safety-tests.md`

## Security & compliance

N/A – test code with disposable containers and credentials.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/user-stories/US-013-cross-backend-assignment-safety-tests.md` (criterion 4, Non-goals)
- `docs/initial-product-specs/rebalancer-product-baseline.md` (Roadmap themes)
- `tests/Rebalancer.IntegrationTests/ZooKeeper/RandomisedTests/`, `tests/Rebalancer.IntegrationTests/PausedNodeScenarios.cs`
