---
id: PRB-0009
status: Open
date: 2026-11-02
opened: 2026-11-02
owner: "dradoaica"
approved-by: [ ]            # closure review; set by a human only
severity: "Medium"
depends-on: [ "ADR-0029", "ADR-0031", "US-0025", "CR-0013", "CR-0014" ]
---

# Problem record: a Redis group sometimes does not converge over a slow, lossy link

## Summary

`RedisNetworkFaultTests` fails intermittently on liveness: after nodes come and go over a degraded link, the group does
not converge within 90 s, on Redis 7.0 and on Valkey 8.1.11. Until CR-0014 the failures were put down to load (US-0025
notes, CR-0013). CR-0014 runs the network-fault tests alone, and the test still failed, so load does not explain it.
The SQL Server and PostgreSQL versions of the same scenario have not failed. No run found two nodes processing a
resource at once.

## Symptoms & impact

- **Symptoms:** "the nodes never converged over the degraded network" or "the nodes did not converge after the churn"
  (`NetworkFaultScenarios`): within 90 s, not every resource is held by exactly one of the running nodes. A group with
  lease expiry 5 s and heartbeat 1 s normally settles within about 10 s.
- **Impact:** unverified. If the cause is in the backend, Redis and Valkey hosts on an unreliable network could have
  resources nobody processes for longer than the lease expiry suggests. If it is in the test (its bounds or the proxy),
  hosts are not affected. Safety is not affected in any run so far.

## Timeline

| When       | Event                                                                                                                    |
|------------|--------------------------------------------------------------------------------------------------------------------------|
| 2026-11-01 | US-0025: one failure on Redis 7.0 in a full run (seed `104995485`); passed alone; attributed to load                     |
| 2026-11-01 | CR-0013: failed in three of four full runs since the Redis suites run on two servers; passed alone                       |
| 2026-11-01 | Cross-check branch: failed twice running alone on Redis 7.0; on `main` (`e62e30c`) failed once alone on Valkey           |
| 2026-11-02 | CR-0014 final run, network-fault tests alone after the others: failed on Valkey (seed `1196549739`), passed on Redis 7.0 |

## Evidence

- `tests/Rebalancer.IntegrationTests/Redis/RedisNetworkFaultTests.cs`: 3 nodes, 6 resources, link through
  `TcpFaultProxy.Degrade(100 ms, 50 ms, 0.02, 500 ms)`, 60 s of churn, 90 s to settle.
- The failing run of 2026-11-02 (seed `1196549739`): churn stops and starts nodes from 22:28:13 to 22:29:14; all three
  run again before the 90 s wait, which expires.
- Failures on clean `main` as well as on the CR-0014 branch: not caused by CR-0014.

## Root cause analysis

Not yet known. To investigate:

- Replay seed `1196549739` (`REBALANCER_RANDOMISED_SEED`) on both servers, with the clients' logs and the monitor's
  per-node view at the deadline: which resources are unheld, and which node is coordinator.
- Whether a dropped or delayed reply leaves a Redis lease or client record in a state the coordinator does not
  recover from (e.g. a keep-alive or `CompareAndSetAsync` retry after a timeout), versus the SQL Server and PostgreSQL
  backends, whose transactions roll back.
- Whether `TcpFaultProxy` can stall a Redis multiplexer connection for good (StackExchange.Redis keeps one connection
  per node and reconnects on its own schedule), which SQL Server and PostgreSQL connection pools would not show.

### Contributing factors

- The failure was attributed to load each time it appeared (US-0025, CR-0013), so it was not investigated.

## Corrective actions

| Action                                                                                     | Owner     | Status |
|--------------------------------------------------------------------------------------------|-----------|--------|
| Reproduce with seed `1196549739` and find the root cause (backend, client library or test) | dradoaica | Open   |
| Fix the cause; if it is in the backend, a regression test that fails before the fix        |           | Open   |

## Preventive measures

- **Agent Governance:** to decide at closure: a liveness failure that recurs is recorded as a problem record at the
  second occurrence, not put down to load.
- **Tests / automation:** the network-fault tests run alone (CR-0014).
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** depends on the root cause.
- **Decision records:** depends on the root cause.

## Known errors / workarounds

Rerun the test alone. Hosts: none needed unless the root cause is in the backend.

## Closure

- **Closed:** (open)
- **Resolution summary:** (open)
- **Preventive action verification:** (open)

## Sources

- `docs/user-stories/US-0025-redis-backend-on-valkey.md` (notes: the first failure, attributed to load)
- `docs/change-records/CR-0013-release-5-0-0.md` (Preconditions: failures in full runs)
- `docs/change-records/CR-0014-release-5-1-0.md` (Preconditions: the failure when running alone)
- `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md`
