---
id: ADR-0006
status: Superseded
date: 2026-10-18
decides: "2.1 contract changes: provider ownership, Redis lease timing, and opt-in ZooKeeper ACLs"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0001", "ADR-0002", "ADR-0003", "US-003", "US-006", "US-007" ]
supersedes: [ ]
superseded-by: "ADR-0016"
---

# Provider resource ownership, Redis server-time leases, and ZooKeeper ACLs

## Context and Problem Statement

Uncommitted hardening work in the working tree (2026-10-18) changes public contracts and a security control. Under
`.agents/AGENTS.md` → *Change classification triage matrix*, these are **Tier 1** and need an accepted decision record
before release. This ADR records them as one decision.

| # | Change (as in working tree)                                                                                                         | Contract affected                                                                      |
|---|-------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|
| 1 | `RedisProvider` implements `IDisposable` and closes its `ConnectionMultiplexer`                                                     | Public type surface (additive)                                                         |
| 2 | `RebalancerClient.Dispose()` disposes the provider when it implements `IDisposable`, also after `StopAsync`                         | Ownership: the client now owns an injected provider                                    |
| 3 | Redis lease and keep-alive times use Redis server time (`TIME`), and lease/client updates use optimistic `WATCH`-style transactions | Behavior; stored data format unchanged (`Dictionary:` / `List:` keys, Newtonsoft JSON) |
| 4 | `ZooKeeperService(string, List<ACL> acl = null, string authScheme = null, byte[] authData = null)` applies an ACL to every znode    | Public constructor signature (binary-breaking) and a **security control**              |

## Decision Drivers

* Leases must not depend on client clock skew, and two coordinators must not both win a lease (ADR 0002 fencing).
* Long-running hosts must not leak a Redis connection per `RebalancerClient`.
* ZooKeeper znodes are created with `OPEN_ACL_UNSAFE`, so anyone who can reach the ensemble can rewrite assignments.

## Considered Options

* **A. Accept the changes as they are in the working tree.**
* **B. Accept 1–3; for 4, keep the old constructor as an overload and add the ACL overload**, so binaries compiled against
  the old constructor keep working, and expose the ACL options on `ZooKeeperProvider` and the DI extension, so users can
  reach them without constructing `ZooKeeperService` themselves.
* **C. Do not change ownership (2)**: callers dispose providers themselves.

## Decision Outcome

Chosen option: **B**. The strongest argument against B is that it adds overloads to keep compatible with a
constructor that few callers use directly. The constructor is still public API, though, and the ACL is only useful if
`ZooKeeperProvider` users can set it: today `ZooKeeperProvider` always calls `new ZooKeeperService(zookeeperHosts)` and
so always uses `OPEN_ACL_UNSAFE`.

### Consequences

* Good, because lease expiry is evaluated on one clock and lease writes are compare-and-set.
* Good, because a disposed client releases its Redis connection.
* Bad, because (2) changes ownership: a caller that shares one provider instance between clients, or disposes it
  separately, must not rely on it staying usable after a client is disposed. DI registrations are transient, so DI users
  are not affected. With transient DI registrations the container may also dispose the provider; this relies on
  `ConnectionMultiplexer.Dispose` tolerating a second call (unverified – please confirm).
* Bad, because ACL misconfiguration (e.g., `CREATOR_ALL_ACL` with different credentials per node) can make nodes unable to
  modify each other's znodes; this needs a short operator note.

### Confirmation

* Unit: `tests/Rebalancer.UnitTests/RedisLeaseDecisionTests.cs`, `RebalancerClientLifecycleTests.cs`.
* Integration (Docker): `tests/Rebalancer.IntegrationTests/Redis/`, `tests/Rebalancer.IntegrationTests/ZooKeeper/`.
* A change record in `docs/change-records/` with the migration note for (2) and (4) at release.

## More Information

* Builds on [ADR 0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md),
  [ADR 0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md),
  [ADR 0003](0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md).
* Stories: US-003 (shutdown), US-006 (Redis), US-007 (ZooKeeper).
