---
id: ADR-0008
status: Accepted
date: 2026-10-18
decides: "JSON serialization dependency of the Redis provider"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0006", "US-006" ]
supersedes: [ ]
superseded-by: ""
---

# Replace the `StackExchange.Redis.DataTypes` dependency with a direct, patched `Newtonsoft.Json` reference

## Context and Problem Statement

Since the Redis services moved to `RedisHash` (atomic, async hash and list access), no code in `src/Rebalancer.Redis`
uses `StackExchange.Redis.DataTypes` any more. The package is still referenced, and it is the only way
`Newtonsoft.Json` reaches the project:

* `RedisHash` serializes with `Newtonsoft.Json` but does not reference it directly.
* `StackExchange.Redis.DataTypes 2.0.0` depends on `Newtonsoft.Json 12.0.1` and `StackExchange.Redis 2.0.519`.
* `Newtonsoft.Json 12.0.1` has a known high-severity advisory (GHSA-5crp-9r3c-p9vr), which the build reports (NU1903).
* The package (2.0.0) is old; its maintenance status is unverified – please confirm.

Every consumer of `Rebalancer.Redis` inherits the vulnerable `Newtonsoft.Json` version unless it overrides it.

## Decision Drivers

* No known-vulnerable dependency in the published package graph.
* Keep the stored Redis format readable: `Dictionary:` / `List:` keys and Newtonsoft JSON encoding (verified on
  2026-10-18 from the package's string literals).
* Do not depend on a package the code does not use.

## Considered Options

* **A. Remove `StackExchange.Redis.DataTypes` from `src/Rebalancer.Redis`; add a direct `Newtonsoft.Json` 13.0.x reference.**
  `13.0.1` and `13.0.3` are in the local NuGet cache; the exact version is to be chosen at implementation, unverified –
  please confirm.
* **B. Keep the package and pin a patched `Newtonsoft.Json` directly** (overrides the transitive 12.0.1).
* **C. Keep everything as is.**

## Decision Outcome

Chosen option: **A**. The strongest argument against A is that some consumer might use the `RedisDictionary` /
`RedisList` types through our transitive reference. Those types were never part of the Rebalancer API, and a consumer
that uses them should reference the package itself. The integration tests do use them to seed data. They keep their
own direct reference (`tests/Rebalancer.IntegrationTests` already has one), so the tests still check that data written
in the old format is readable.

### Consequences

* Good, because the published package no longer pulls a known-vulnerable `Newtonsoft.Json` and has one dependency less.
* Bad, because a consumer that relied on the transitive `RedisDictionary` types must add the package itself (release note).

### Confirmation

* `dotnet build` shows no NU1903 for `Newtonsoft.Json` from `Rebalancer.Redis`.
* `RedisAssignmentSafetyTests` (seeded with `RedisDictionary` / `RedisList`) still passes, proving format compatibility.

## More Information

* Related: [ADR 0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md),
  [ADR 0006](0006-provider-resource-ownership-and-zookeeper-acl.md), US-006.
