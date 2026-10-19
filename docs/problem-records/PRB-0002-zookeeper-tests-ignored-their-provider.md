---
id: PRB-0002
status: Closed
date: 2026-10-18
opened: 2026-10-18
owner: ""
approved-by: [ "dradoaica" ]            # closure review; set by a human only (dradoaica, in session 2026-10-19, retroactive)
severity: "High (false confidence in assignment-safety coverage)"
depends-on: [ "ADR-0001", "ADR-0003", "ADR-0009", "US-010", "CR-0001" ]
---

# Problem record: ZooKeeper integration tests ran with the first test's provider

## Summary

In one test process, every ZooKeeper integration test built its clients from the provider factory of whichever test ran
first. Tests named `ResourceBarrier_*` could run GlobalBarrier providers (and the reverse), and the randomised tests
used the first test's timeouts and rebalancing interval. The suite passed while one of the two rebalancing modes was
not exercised.

## Symptoms & impact

- **Symptoms:** In a 14-test run of `SingleClientTests` + `MultiClientTests` on 2026-10-18, the GlobalBarrier-only log
  line `Coordinator - Get active clients and resources` appeared 40 times. The ResourceBarrier-only line
  `Coordinator - Get clients and resources list` appeared 0 times, although 7 of those tests are named `ResourceBarrier_*`.
- **Impact:** US-010 and ADR 0003 cite these tests as evidence that both modes uphold the safety invariant; for
  ResourceBarrier that evidence did not exist. The randomised suite's per-test configurations were also not applied.

## Timeline

| When       | Event                                                                                              |
|------------|----------------------------------------------------------------------------------------------------|
| 2026-10-18 | Detected while preparing the randomised suite run; confirmed from the log of the earlier run       |
| 2026-10-18 | Tests changed to pass a per-test provider factory to `RebalancerClient` (uncommitted)              |

## Evidence

- Log counts above, from the run of `SingleClientTests` and `MultiClientTests` against a local 3-node ensemble.
- `src/Rebalancer.Core/Providers.cs`: `Register` keeps only the first factory.
- After the fix (2026-10-18), `SingleClientTests` + `MultiClientTests` + `AclTests`: 18/18 passed, with the
  ResourceBarrier-only log line present (6 times in the first 9 tests, previously 0). This includes the 4 multi-client
  tests that had failed both on `HEAD` and on the working tree because of a startup race in their assertions.
- Second defect found during the same run: the randomised tests wrote their event log to a hardcoded path on the
  original author's machine (`/home/jack/tmp/rebalancer-zk/...`) on every iteration. On any other machine every
  randomised test failed with `DirectoryNotFoundException` before reaching its assertions, so the suite had not been
  effectively run elsewhere.

## Root cause analysis

`Providers.Register` is static and keeps only the first registration per process; later calls are ignored without an
error. ADR 0001 records this as a known drawback ("can surprise tests"). Each test called
`Providers.Register(...)` and then `new RebalancerClient()`, which resolves the first registered factory. That
factory is also bound to the first test class instance, so the randomised tests read that instance's `currentConfig`.

### Contributing factors

- The silent first-wins behavior gives no signal.
- No test asserted which rebalancing mode actually ran.

## Corrective actions

| Action                                                                                                                     | Owner | Status             |
|----------------------------------------------------------------------------------------------------------------------------|-------|--------------------|
| ZooKeeper tests pass their own factory: `new RebalancerClient(providerFactory())` (`SingleClientTests`, `MultiClientTests`, randomised `TestClient`) | | Done (2.1.0)       |
| Re-run the ZooKeeper suites so both modes are actually exercised                                                           |       | Done: single/multi/ACL all passed; randomised smoke (2-min cap) 6/13 passed, 0 failed, both modes, then stopped by decision of dradoaica as sufficient |
| Startup-race assertions in 4 multi-client tests replaced by settled-outcome checks (`AssertSettledAssigned`)                |       | Done (2.1.0)       |
| Randomised tests write their event log under the OS temp directory; `REBALANCER_RANDOMISED_TEST_MAX_MINUTES` caps runtime   |       | Done (2.1.0)       |
| Release with `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md`                              |       | Done (2.1.0)       |

## Preventive measures

- **Agent Governance:** N/A. This is a test defect; the existing regression-test rules apply. The static registry is
  already a recorded ADR 0001 drawback.
- **Tests / automation:** New tests construct `RebalancerClient` with an explicit provider; no test uses `Providers`.
- **Monitoring / alerts:** N/A (test suite).
- **Runbooks / docs:** None.
- **Decision records:** `docs/adrs/0009-reject-conflicting-provider-registrations.md` (Accepted, implemented): a second,
  different `Providers.Register` call now throws instead of being silently ignored, so this cannot recur unnoticed.

## Known errors / workarounds

None after the fix.

## Closure

- **Closed:** 2026-10-18
- **Resolution summary:** Fixed in 2.1.0 (`2952de6`): every ZooKeeper test passes its own provider to
  `RebalancerClient`; both barrier modes verified by log evidence; the randomised tests no longer depend on a
  developer-specific path.
- **Preventive action verification:** ADR 0009 (implemented in 2.1.0) makes a conflicting `Providers.Register` call
  throw, so this failure mode cannot recur silently. Governance change: N/A (test defect; root cause addressed in the
  library).

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md` (Consequences: first registration wins)
- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- `docs/user-stories/US-010-zookeeper-tests-uphold-assignment-safety.md`
- `tests/Rebalancer.IntegrationTests/ZooKeeper/`
