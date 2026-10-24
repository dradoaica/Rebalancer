---
id: US-0024
status: In progress
date: 2026-10-24
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-24)
priority: ""
ssdlc-phase: Testing
depends-on: [ "ADR-0002", "ADR-0019", "ADR-0021", "US-0013", "US-0019", "CR-0011" ]
---

# User story: Randomised SQL Server / Redis tests check what nodes process, not only what they report

## Story

**As a** maintainer  
**I want** the randomised SQL Server and Redis tests (US-0019) to check what each node's handlers process, from
`OnAssignment` to the `OnUnassignment` that ends it  
**So that** a defect where a node keeps processing after the library considers it unassigned fails the suite, as CR-0011's
did not

## Context

`RandomisedScenario` (US-0019) samples `GetAssignedResources()` on every node every 100 ms: it checks the provider's own
view. CR-0011 found that view and the handlers disagreeing: on a clean `StopAsync` a SQL Server or Redis node cleared its
view but never raised `OnUnassignment`, so a host kept processing while the coordinator gave the resources to another
node. The randomised suite passed throughout; the network fault tests found it because they check the handlers with
`ProcessingMonitor`, and CR-0011 confirmed it by re-running randomised seeds with a clean stop treated as the end of
processing.

`ProcessingMonitor` cannot be attached as it is, because of pauses. `NodePause` freezes only a node's store calls; its
handlers keep running, which a real process pause would also freeze (see `NodePause`'s summary). The monitor would see a
paused node processing throughout its pause and report the overlap with the node that took over, which ADR-0019 and
ADR-0021 accept only from the resume until the node's first tick. The monitor must therefore know about pauses, and the
accepted window must be explicit rather than a blanket exclusion of resumed nodes, as `ResumeGrace` is today.

**Decision for the reviewer: add, or replace?** Recommended: add the handler-level checks and keep the provider-view
checks. They test different contracts: what hosts are told (`OnAssignment` / `OnUnassignment`) and what
`GetAssignedResources()` reports (US-0003, ADR-0007). Replacing would drop the second. The strongest argument for
replacing: two checks on one run give two failure modes to triage, and the provider view adds little once the handlers
are checked. Each failure message must say which view failed (criterion 4).

Tier 3 (a test improvement within ADR-0002, ADR-0019 and ADR-0021; no library change). The reviewer confirms or corrects
it.

## Acceptance criteria

1. `RandomisedScenario` attaches a `ProcessingMonitor` to every node it starts. A kill calls `Crash(node)`. A pause and
   its resume are reported to the monitor, which counts no processing for the node while it is paused and resumes it
   for the resources the node processed before the pause.
2. **Safety (handlers):** the run fails on any moment two nodes process the same resource, except a resumed node
   processing a resource it already processed before its pause, for at most `ResumeGrace` (3 s, the ADR-0019 window
   that ADR-0021 accepts) after its resume. The failure names the seed, both nodes, the resource and the times.
3. **Liveness (handlers):** the run fails when a resource is processed by no running, unpaused node for longer than
   `UnassignedBound` (45 s), while at least one such node runs.
4. Per the reviewer's decision above: if kept, the provider-view checks (`GetAssignedResources()` sampling) run as today, and every
   failure message says whether the handler view or the provider view failed.
5. The pause and window rules of `ProcessingMonitor` have tests that need no backend (scripted calls to its methods):
   an overlap inside the window passes, one that outlasts it fails, an overlap on a resource the resumed node did not
   process before its pause fails, and a crashed node never counts again.
6. **The check has teeth:** with CR-0011's fix reverted locally (a clean stop that does not raise `OnUnassignment`, as
   in 4.3.0), the suite fails on both SQL Server and Redis for at least one seed; the seeds are recorded in the MR/PR
   and pass with the fix.
7. Unchanged: `REBALANCER_RANDOMISED_SEED`, `REBALANCER_RANDOMISED_TEST_MAX_MINUTES`, the default duration, node and
   resource counts, and the `*NetworkFaultTests` (which also use `ProcessingMonitor`) still pass.
8. `RandomisedScenario`'s summary, the README's test description and `docs/traceability-matrix.md` (US-0019 / US-0024)
   describe the handler-level checks.

## Non-goals

- `*AssignmentSafetyTests` and `*PausedNodeTests`, which also sample `GetAssignedResources()`; candidates for a
  follow-up once this lands.
- The ZooKeeper randomised suite (its own `ResourceMonitor` already records assignment events).
- Narrowing or closing the ADR-0019 window; it stays as ADR-0021 decided.
- Running the suite in CI (no CI test job is planned; see the coverage review of 2026-10-24).
- Any library change. A defect the new checks find gets its own problem record.

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`,
  `docs/adrs/0021-assignment-generation-for-resource-handlers.md` (the accepted window)
- Other stories / records: `docs/user-stories/US-0019-randomised-assignment-safety-tests-for-sql-server-and-redis.md`
  (the suite), `US-0013-cross-backend-assignment-safety-tests.md`,
  `docs/change-records/CR-0011-release-4-3-1.md` (the defect the suite missed)

## Security & compliance

N/A: test code only, against disposable containers (Testcontainers) with test credentials; no production data or
connection strings.

## Definition of ready

<!-- Ticked items verified by an agent against this story's text on 2026-10-24; readiness approved by dradoaica on
2026-10-24 (approved-by). -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified
- [x] Reviewer decision recorded: add the handler-level checks, keeping the provider-view checks (criterion 4;
  the recommended option, approved with the story by dradoaica on 2026-10-24)

## Implementation notes (2026-10-24, branch `agent/prb-0006-0007-us-0024`)

- Criteria 1–3: `ProcessingMonitor` gained `Pause`, `Resume(node, window)`, `CheckWindows` and `IsProcessed`; handler
  events of a paused node take effect at its resume, since `NodePause` lets calls in flight complete.
  `RandomisedScenario` attaches it to every node and checks both views every 100 ms.
- Criterion 4: provider-view checks kept; messages say "(handler view)" or "(provider view)".
- Criterion 5: `tests/Rebalancer.IntegrationTests/ProcessingMonitorTests.cs` (5 tests, manual clock, no backend).
- Criterion 6: with CR-0011's fix reverted locally (`InvokeOnStopForCleanStop` returning at once), both suites failed
  in the handler view: Redis seed `1703825424` after 20 s (node 0 stopped, node 3 started r8 and r9 while node 0 still
  processed them), SQL Server seed `622234822` after 77 s (node 1 stopped, node 0 started r1 while node 1 still
  processed it). With the fix both seeds pass a full 3-minute run.
- Criterion 7: seed and duration handling unchanged; with the changed monitor, `SqlServerNetworkFaultTests`,
  `RedisNetworkFaultTests` and `ZooKeeper/NetworkFaultTests` (14 cases) pass.
- Criterion 8: `RandomisedScenario` summary, README and traceability matrix updated.

## Definition of done

<!-- 2026-10-24: "Acceptance criteria met" and "Docs" ticked by an agent against the implementation notes above; the
other two need the human review and the release. -->

- [x] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/CR-0012-release-4-3-2.md` (4.3.2, Submitted)

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`
- `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`
- `docs/adrs/0021-assignment-generation-for-resource-handlers.md` (Decision Outcome, Consequences)
- `docs/user-stories/US-0013-cross-backend-assignment-safety-tests.md`,
  `docs/user-stories/US-0019-randomised-assignment-safety-tests-for-sql-server-and-redis.md`
- `docs/change-records/CR-0011-release-4-3-1.md` (Motivation)
- `tests/Rebalancer.IntegrationTests/RandomisedScenario.cs`, `ProcessingMonitor.cs`, `NodePause.cs`,
  `NetworkFaultScenarios.cs`
