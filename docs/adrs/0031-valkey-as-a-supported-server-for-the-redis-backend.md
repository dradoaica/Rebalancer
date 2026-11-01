---
id: ADR-0031
status: Accepted
date: 2026-11-01
decides: "which servers the Redis backend supports besides Redis"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0017", "ADR-0020", "ADR-0029", "TM-0003" ]
supersedes: [ ]
superseded-by: ""
---

# Valkey as a supported server for the Redis backend

## Context and Problem Statement

`Rebalancer.Redis` is tested against Redis only (`redis:7.0` in `tests/Rebalancer.IntegrationTests/DockerSupport.cs`).
Valkey is a fork of Redis, so a deployment that has moved to it needs to know whether Rebalancer works there. The
backend uses a small set of commands: hash and list reads and writes, `MULTI` / `EXEC` transactions with the
conditions of StackExchange.Redis (`WATCH`), and `TIME` for the lease and keep-alive clock (ADR-0017). Its keys are
per group since 4.0 (ADR-0020), and the minimal ACL user in `docs/release-notes/migrating-to-4.0.md` grants exactly
these commands.

This record decides whether the Redis backend supports Valkey, and what makes that claim true.

## Considered Options

* **A. Support Valkey through the existing Redis backend**, confirmed by running the Redis integration suite against
  a pinned Valkey image.
* **B. A separate `Rebalancer.Valkey` package.**
* **C. Make no claim:** users try Valkey at their own risk.

## Decision Outcome

Chosen option: "A. Support Valkey through the existing Redis backend", because nothing in the backend is specific to
Redis beyond the commands above, so a second package (B) would duplicate `Rebalancer.Redis` without a difference to
put in it, and C leaves users to find out about a lease or transaction difference in production.

* **No code change.** `RedisProvider` connects to Valkey with the same connection string format through
  StackExchange.Redis (compatibility unverified – please confirm; this record's confirmation settles it).
* **Tests:** the Redis integration suites (assignment safety, randomised, paused node, network fault, assignment delay,
  aborted state, client records, per-group keys, health check, telemetry) also run against a pinned Valkey image, with
  the same assertions; the image is pinned in `DockerSupport.cs` next to `RedisImage` (tag unverified – please
  confirm). The minimal ACL user from the migration guide is checked on Valkey as well.
* **Supported versions** are the Valkey versions the suite has passed on, named in `README.md` and the release notes;
  other versions are not claimed.
* **Threat model:** TM-0003 applies unchanged if Valkey's ACLs and `TIME` behave as Redis's; a difference found by the
  tests is a TM-0003 revision.
* **Release:** a 5.x minor version or patch with the tests and documentation; no package changes.

### Confirmation

The decision holds only if the suites pass on Valkey unchanged. If one fails because of a server difference, the
difference is recorded here as a note, and this record is superseded by one that either changes the backend or
rejects Valkey support.

### Consequences

* Good, because Valkey users get a tested statement instead of a guess, at the cost of tests and documentation.
* Good, because the same approach can later cover other Redis-compatible servers, each with its own record.
* Bad, because every Redis backend change must now pass on two servers, and the test run starts one more container.
* Bad, because Redis and Valkey may diverge in later versions; the pinned images keep the claim narrow, and a newer
  Valkey version is supported only after the suite passes on it.

## Sources

- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (Decision Outcome: `TIME` and compare-and-set)
- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (per-group keys)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (the backend supplies only its services)
- `docs/threat-models/TM-0003-redis-coordination.md` (Redis coordination threats, ACL user)
