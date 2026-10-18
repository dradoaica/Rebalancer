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
3. The root `README.md` documents how to run the ZooKeeper tests locally: the 3-node cluster on `localhost:2181-2183` they expect,
   and `REBALANCER_RANDOMISED_TEST_MAX_MINUTES` to cap the randomised tests for a smoke run. CI integration remains out of scope.

## Non-goals

- Formal TLA+ proof linkage (mentioned in product README as future/parallel work).
- Multi-machine soak tests unless already part of the repository.

## Dependencies

- Decision records: `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- Other stories: `docs/user-stories/US-007-coordinate-via-zookeeper-with-barrier-mode.md`

## Security & compliance

Tests may use local ZK instances; ensure test credentials and ports are not exposed in CI logs. N/A for production data.

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
- [x] Traceable to change record (if released) — `docs/change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md` (2.1.0)

## Sources

- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- `src/Rebalancer.ZooKeeper/README.md`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/RandomisedTests/RandomisedResourceBarrierTests.cs`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/SingleClientTests.cs`, `MultiClientTests.cs` (as applicable)
