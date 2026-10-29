---
id: ADR-0016
status: Accepted
date: 2026-10-18
decides: "ownership and disposal of the provider passed to a RebalancerClient"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0006", "US-0003", "US-0006" ]
supersedes: [ "ADR-0006" ]
superseded-by: ""
---

# `RebalancerClient` owns and disposes its provider

## Context and Problem Statement

ADR-0006 decided four things in one record; the repository standard now allows one concern per decision record. This
record carries over ADR-0006 items 1 and 2 **unchanged**, so ADR-0006 can be superseded by one-concern records (this one, ADR-0017 and ADR-0018). It introduces no new decision.

Long-running hosts must not leak a Redis connection per `RebalancerClient`, and a provider used by one client should be
released with that client.

## Decision Drivers

* No connection leak per disposed client.
* One clear owner for a provider's resources.

## Considered Options

* **A. The client owns its provider** (as decided in ADR-0006, option B, items 1–2).
* **B. Callers dispose providers themselves** (ADR-0006 option C, rejected there).

## Decision Outcome

Proposed option: **A**, as already in force since 2.1.0:

1. `RedisProvider` implements `IDisposable` and closes its `ConnectionMultiplexer`.
2. `RebalancerClient.Dispose()` disposes the provider when it implements `IDisposable`, also after `StopAsync`.

The strongest argument against A, as recorded in ADR-0006: a caller that shares one provider instance between clients,
or disposes it separately, must not rely on it staying usable after a client is disposed. One provider per client is the
supported pattern, and the DI registrations are transient.

### Consequences

* Good, because a disposed client releases its Redis connection.
* Bad, because sharing a provider between clients is unsupported. With transient DI registrations the container may
  also dispose the provider; this relies on `ConnectionMultiplexer.Dispose` tolerating a second call (unverified –
  please confirm), as already noted in ADR-0006.

### Confirmation

* `tests/Rebalancer.UnitTests/RebalancerClientLifecycleTests.cs` (existing).

## More Information

* Replaces items 1–2 of [ADR-0006](0006-provider-resource-ownership-and-zookeeper-acl.md); together with
  [ADR-0017](0017-redis-lease-timing-and-atomic-updates.md) and [ADR-0018](0018-opt-in-zookeeper-acls-in-2x.md) it
  supersedes ADR-0006. On acceptance of all three, a human sets ADR-0006 to `Superseded`. Its single
  `superseded-by` field cannot list three records; record it as `superseded-by: "ADR-0016"` and name ADR-0017 and
  ADR-0018 here.
* Appended 2026-10-20 at dradoaica's request: `superseded-by` is now a list, and ADR-0006 lists ADR-0016, ADR-0017
  and ADR-0018. The decision is unchanged.
* Appended 2026-10-29 at dradoaica's request: `superseded-by` is a single identifier again, so ADR-0006 records
  `superseded-by: "ADR-0016"` as first planned above; ADR-0017 and ADR-0018 also supersede it. The decision is
  unchanged.
* No code change.

## Sources

- `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md` (ADR-0006: Provider resource ownership, Redis
  server-time leases, and ZooKeeper ACLs)
- `docs/user-stories/US-0003-graceful-shutdown-and-read-assignment-state.md` (US-0003: Graceful shutdown and read
  assignment state)
- `docs/user-stories/US-0006-coordinate-via-redis-backend.md` (US-0006: Coordinate via Redis backend)
