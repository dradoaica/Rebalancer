---
id: PRB-0009
status: Root cause identified
date: 2026-11-02
opened: 2026-11-02
owner: "dradoaica"
approved-by: [ ]            # closure review; set by a human only
severity: "Medium"
depends-on: [ "ADR-0017", "ADR-0026", "ADR-0029", "ADR-0031", "US-0025", "CR-0013", "CR-0014" ]
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
- **Impact:** the cause is in the Redis backend (see Root cause analysis). A Redis or Valkey group whose store round
  trip is a sizeable fraction of its heartbeat (here about 150 ms against 1 s) can keep rebalancing without settling,
  so resources stay unprocessed for minutes. Groups with the usual heartbeats of several seconds on a local network
  are unlikely to see it: the conflict needs a keep-alive to land inside a write attempt. Safety is not affected: a
  failed attempt writes nothing, and no run found two nodes processing a resource at once.

## Timeline

| When       | Event                                                                                                                    |
|------------|--------------------------------------------------------------------------------------------------------------------------|
| 2026-11-01 | US-0025: one failure on Redis 7.0 in a full run (seed `104995485`); passed alone; attributed to load                     |
| 2026-11-01 | CR-0013: failed in three of four full runs since the Redis suites run on two servers; passed alone                       |
| 2026-11-01 | Cross-check branch: failed twice running alone on Redis 7.0; on `main` (`e62e30c`) failed once alone on Valkey           |
| 2026-11-02 | CR-0014 final run, network-fault tests alone after the others: failed on Valkey (seed `1196549739`), passed on Redis 7.0 |
| 2026-11-02 | Replays of seed `1196549739` with diagnostics: 3 of 6 failed (both servers), so the cause is timing, not the churn       |
| 2026-11-02 | Root cause identified with a trace of every failed coordinator write (75 failed attempts, see Evidence)                  |

## Evidence

- `tests/Rebalancer.IntegrationTests/Redis/RedisNetworkFaultTests.cs`: 3 nodes, 6 resources, link through
  `TcpFaultProxy.Degrade(100 ms, 50 ms, 0.02, 500 ms)`, 60 s of churn, 90 s to settle.
- The failing run of 2026-11-02 (seed `1196549739`): churn stops and starts nodes from 22:28:13 to 22:29:14; all three
  run again before the 90 s wait, which expires.
- Failures on clean `main` as well as on the CR-0014 branch: not caused by CR-0014.
- A failed replay (2026-11-02, Redis 7.0 and Valkey): at the deadline all three nodes reported `PendingAssignment`
  and processed nothing. The lease named client `c198d70f` and had just been renewed; that client's own record had
  its last keep-alive 18 s earlier, and its log showed four aborts within a minute, each "RebalancerException: Could
  not modify clients due to concurrent modifications", each followed by a restart and a new rebalance.
- A trace of every failed `ModifyClientsAsync` attempt over one replay on both servers (temporary instrumentation,
  not committed; patch kept outside the repository): 75 failed attempts, each 640–2143 ms (median 966 ms). In every one
  at least one of the client records it read had changed before `EXEC` (54 only client records, 21 client records and
  the lease); in none only the lease. Three times all 10 attempts failed and the coordinator aborted.

## Root cause analysis

**Optimistic writes of all client records at once lose to the keep-alives.** The Redis coordinator grants and stops
activity with `ClientService.ModifyClientsAsync` (`src/Rebalancer.Redis/Clients/ClientService.cs`): it reads every
target client record and the lease, then runs one `MULTI/EXEC` conditioned on all of them being unchanged (`Condition.HashEqual`, ADR-0017's compare-and-set; ADR-0026's lease guard), and gives up after 10 attempts. Those
values change all the time:

- every node's keep-alive rewrites its own record once per heartbeat (`LastKeepAlive`), and a rebalance writes every
  node's record;
- the coordinator's own renewal loop rewrites the lease field (`LastCoordinatorRenewal`) several times per heartbeat.

On a fast link an attempt takes milliseconds and rarely meets a change. Over the test's link an attempt is several
round trips (`HMGET`, the lease read, `WATCH`/`MULTI`/`EXEC`) and takes about a second, as long as the heartbeat, so
most attempts meet a keep-alive. When all 10 fail, the coordinator's role throws, the node restarts (`AutoRecoveryOnError`), and the next rebalance meets the same odds: a livelock that ends only by luck. SQL Server and
PostgreSQL take row locks, so a writer waits instead of losing; that is why only Redis fails.

The test's proxy, the client library and load are not the cause: the failure reproduces alone, on both servers, and
the trace shows the conflicts directly.

### Contributing factors

- The failure was attributed to load each time it appeared (US-0025, CR-0013), so it was not investigated.
- The compare-and-set covers whole client records, including `LastKeepAlive`, which the coordinator does not need
  unchanged: only the record's existence, its fencing token and its status matter to the write.

## Corrective actions

| Action                                                                                         | Owner     | Status |
|------------------------------------------------------------------------------------------------|-----------|--------|
| Reproduce with seed `1196549739` and find the root cause (backend, client library or test)     | dradoaica | Done   |
| Decide the fix (a decision record: it changes ADR-0017's compare-and-set or the stored format) | dradoaica | Open   |
| Fix the cause, with a regression test that fails before the fix                                |           | Open   |

## Preventive measures

- **Agent Governance:** to decide at closure: a liveness failure that recurs is recorded as a problem record at the
  second occurrence, not put down to load.
- **Tests / automation:** the network-fault tests run alone (CR-0014).
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** until the fix, the README could advise Redis users to keep the heartbeat well above the store
  round trip (to decide with the fix).
- **Decision records:** needed; see Corrective actions.

## Known errors / workarounds

Hosts: keep the group's heartbeat and lease expiry well above the Redis round trip, so that keep-alives rarely land
inside a write attempt. Tests: the failure is intermittent; rerun.

## Closure

- **Closed:** (open)
- **Resolution summary:** (open)
- **Preventive action verification:** (open)

## Sources

- `docs/user-stories/US-0025-redis-backend-on-valkey.md` (notes: the first failure, attributed to load)
- `docs/change-records/CR-0013-release-5-0-0.md` (Preconditions: failures in full runs)
- `docs/change-records/CR-0014-release-5-1-0.md` (Preconditions: the failure when running alone)
- `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md`
- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (Decision Outcome: compare-and-set transactions)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (Implementation outline, item 1: the lease guard)
