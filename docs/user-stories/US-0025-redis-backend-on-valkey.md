---
id: US-0025
status: In progress
date: 2026-11-01
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-11-01)
priority: ""
ssdlc-phase: Coding
depends-on: [ "ADR-0031", "ADR-0017", "ADR-0020", "TM-0003", "US-0006" ]
---

# User story: The Redis backend is tested and supported on Valkey

## Story

**As a** team that runs Valkey instead of Redis  
**I want** `Rebalancer.Redis` to be tested on a named Valkey version, with the result documented  
**So that** I can use Rebalancer on Valkey knowing its lease, transaction and ACL behavior was checked, not assumed

## Context

ADR-0031 (Accepted) decides that the Redis backend supports Valkey with no code change, once the Redis integration
suites pass on a pinned Valkey image with the same assertions. Today every Redis test starts its own container with
`new RedisBuilder(DockerSupport.RedisImage)` (`redis:7.0`).

**Decided (dradoaica, 2026-11-01): the suites run on both servers by default.** Each Redis integration test becomes an
xUnit theory over the servers in `DockerSupport` (Redis and Valkey), so the default test run covers both. The strongest
argument against, accepted: it roughly doubles the Redis part of the run (54 SQL Server and Redis tests take about 4–6
minutes today), and every new Redis test has to remember the theory. The rejected alternative, an environment variable
that selects the server for a second run, keeps the run short but makes Valkey a run that nobody does by default, which
is the gap ADR-0031 closes.

Whether Testcontainers' `RedisBuilder` starts a Valkey image (it waits for Redis's log line and readiness check) is
unverified – please confirm; if it does not, a generic container with the same wait is used instead.

Tier 2 (a product support claim within ADR-0031; tests and docs, no library change). The reviewer confirms or corrects
it.

## Acceptance criteria

1. `DockerSupport` names the Valkey image next to `RedisImage`, pinned to one tag (to be chosen in the MR/PR; no
   `latest`).
2. Every test class under `tests/Rebalancer.IntegrationTests/Redis/` runs against both Redis and the pinned Valkey
   image in the default run (xUnit theories over the servers in `DockerSupport`). Test names or output say which
   server ran.
3. All of them pass on Valkey with their assertions unchanged. A failure that comes from a server difference is not
   worked around in the tests: it is recorded as a note on ADR-0031 and stops this story (ADR-0031, Confirmation).
4. The minimal per-group ACL user from `docs/release-notes/migrating-to-4.0.md` is checked on Valkey: a node using only
   that user starts, is assigned, and stops cleanly (`RedisClientRecordTests.ARebalancerClient_WorksEndToEnd_WithTheMinimalPerGroupUser`, run on both servers).
5. `README.md` (Languages and Backends) and the release notes of the release that ships this name the supported
   Valkey version (s); the traceability matrix row of ADR-0031 points to the tests.
6. The SQL Server, ZooKeeper and unit tests are unchanged and still pass.

## Implementation notes for the reviewer (2026-11-01)

1. Pinned image: `valkey/valkey:8.1.11` (the newest 8.1 patch on Docker Hub that day). Testcontainers' `RedisBuilder`
   starts it unchanged.
2. Result: all 25 Redis tests passed on Valkey. One run of the doubled suite failed one Redis 7.0 test on liveness (`RedisNetworkFaultTests`, seed `104995485`, "the nodes never converged over the degraded network"); the same seed
   passed on both servers when run alone. Running both servers doubles the containers started at once, which makes
   the timing-sensitive tests more likely to miss their bounds on a loaded machine.

## Non-goals

- Any change to `Rebalancer.Redis` or its dependencies. A needed change means a new decision record (ADR-0031).
- Other Redis-compatible servers (Garnet, Dragonfly, KeyDB) and managed services (ElastiCache, Azure Cache for
  Redis); each needs its own record.
- Valkey Cluster or Sentinel deployments; the suites run a single server, as they do for Redis.
- Moving the Redis image off `redis:7.0`.

## Dependencies

- Decision records: `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md` (Accepted),
  `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md`, `docs/adrs/0020-per-group-redis-leases-and-resources.md`
- Threat model: `docs/threat-models/TM-0003-redis-coordination.md`
- Other stories: `docs/user-stories/US-0006-coordinate-via-redis-backend.md`
- Release: 5.0.0 (CR-0013, decided by dradoaica 2026-11-01).

## Security & compliance

TM-0003 applies unchanged if Valkey's ACLs and `TIME` behave as Redis's; criterion 4 checks the ACL part. A difference
found by the tests is a TM-0003 revision, not a test change. No new data, credentials or network exposure: the tests
run local containers.

## Definition of ready

- [x] Governing ADR referenced and `Accepted` on the shared trunk (ADR-0031)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md` (Decision Outcome, Confirmation)
- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (`TIME` and compare-and-set)
- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (per-group keys)
- `docs/threat-models/TM-0003-redis-coordination.md` (ACL user)
- `docs/user-stories/US-0006-coordinate-via-redis-backend.md`
- `tests/Rebalancer.IntegrationTests/DockerSupport.cs` (pinned images)
