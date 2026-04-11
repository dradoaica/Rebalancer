# User story: ZooKeeper tests uphold assignment safety invariants

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-010                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** maintainer  
**I want** automated tests for the ZooKeeper backend—including randomized scenarios—to detect violations of “at most one assignee per resource”  
**So that** regressions in barrier or leader logic are caught before release

## Context

`src/Rebalancer.ZooKeeper/README.md` states core invariants and describes single-process multi-node tests against a real ZooKeeper cluster. `RandomisedResourceBarrierTests` and related suites exercise barrier modes.

## Acceptance criteria

1. Folder `tests/Rebalancer.IntegrationTests/ZooKeeper/` includes randomized barrier tests referenced from ADR 0003.
2. Story acceptance references the invariant: no two clients may hold the same resource assignment concurrently under test conditions (as asserted by tests—cite test names or files in verification).
3. CI or local test instructions remain the team’s responsibility; story only documents intent aligned with existing tests.

## Non-goals

- Formal TLA+ proof linkage (mentioned in product README as future/parallel work).
- Multi-machine soak tests unless already part of the repository.

## Dependencies

- Decision records: `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- Other stories: `docs/user-stories/US-007-coordinate-via-zookeeper-with-barrier-mode.md`

## Security & compliance

Tests may use local ZK instances; ensure test credentials and ports are not exposed in CI logs. N/A for production data.

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- `src/Rebalancer.ZooKeeper/README.md`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/RandomisedTests/RandomisedResourceBarrierTests.cs`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/SingleClientTests.cs`, `MultiClientTests.cs` (as applicable)
