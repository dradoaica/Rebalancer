---
id: ADR-0029
status: Accepted
date: 2026-11-01
decides: "coordination pattern (coordinator, follower, lease, store) for the SQL Server and Redis backends"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0003", "ADR-0022", "ADR-0026" ]
supersedes: [ "ADR-0002" ]
superseded-by: ""
---

# One shared coordination project for the SQL Server and Redis backends

## Context and Problem Statement

ADR-0002 chose parallel implementations of the coordinator / follower / lease / store pattern in
`src/Rebalancer.SqlServer/` and `src/Rebalancer.Redis/`, and named the cost: "bug fixes may need to be applied in two
places; drift risk is mitigated only by discipline and tests". It rejected one generic implementation because "SQL
and Redis semantics differ; a leaky abstraction could hide subtle bugs".

As built, the semantics that differ are only in the data-access services. With the backend names replaced, a diff of
the two projects (2026-11-01) shows:

* identical: `Roles/Follower.cs`, `PendingStart`, `ClientEvent`, `CoordinatorToken`, `EventType`, all of `Store/`, and
  the models and interfaces in `Clients/` and `Leases/` plus `IResourceService` (about 600 lines);
* differing only in the telemetry backend tag: `Roles/Coordinator.cs` (605 lines, 4 differing);
* differing in connection setup, the Redis per-group key migration and the telemetry tag: the providers (about 700
  lines each, 25 differing);
* different: `LeaseService`, `ClientService`, `ResourceService`, `TransientErrorDetector`, `ConnectionHelper`,
  `RedisHash`, `PerGroupKeyMigration`.

Safety fixes already had to be made twice (ADR-0026 changed both `Coordinator.cs` files). This record decides the
coordination pattern of the two backends again, and where its shared code lives.

## Considered Options

* **A. Keep parallel implementations (ADR-0002)** and add a test that fails when the mirrored files differ in more
  than the backend name.
* **B. A new project and package, `Rebalancer.LeaseCoordination`**, holding the roles, the store and the service
  interfaces and models. The backends keep their services and provider.
* **C. Move the shared code into `Rebalancer.Core`.**
* **D. Shared source files** compiled into both backend assemblies, with no new package.

## Decision Outcome

Chosen option: "B. A new project and package", because it is the only option that leaves one copy of the roles and
the store while keeping what ADR-0002 kept for good reasons: each backend still uses its native concurrency
primitives in its own services. The pattern of ADR-0002 (one coordinator under a lease assigns resources; followers
apply them; a per-group store holds clients, resources and assignment state) is unchanged; only where its code lives
changes.

* `Rebalancer.LeaseCoordination` references `Rebalancer.Core` and no backend client library. It contains
  `Coordinator`, `Follower`, `ResourceGroupStore` and the other identical types listed above, and the shared part of
  the provider (the leadership and follower loops) as an internal base class.
* The name says what is shared: coordination under a lease, which the ZooKeeper backend does not use (ADR-0003 uses
  ephemeral znodes and barriers). Rejected names: `Rebalancer.Coordination` (ZooKeeper coordinates too),
  `Rebalancer.Provider` (clashes with `IRebalancerProvider` in `Rebalancer.Core`), `Rebalancer.Backends.Core` (suggests
  every backend builds on it), `Rebalancer.SqlOrKeyValueBackend` (the package contains no data access).
* `Rebalancer.SqlServer` and `Rebalancer.Redis` reference it and keep their `LeaseService`, `ClientService`,
  `ResourceService`, connection and error-detection code, and their public `SqlServerProvider` / `RedisProvider`.
  The backend tag for telemetry (ADR-0022) is passed to the shared code instead of written in it.
* Code is moved, not rewritten: behaviour is unchanged, and the existing unit, randomised and paused-node integration
  tests of both backends pass without change to what they assert.

Option A keeps the duplication and only detects drift. Option C gives every `Rebalancer.Core` user, including
ZooKeeper users, types they do not use. Option D compiles the same internal types into both assemblies, which the test
projects (`InternalsVisibleTo` on both) cannot tell apart, and the shared files would still need the backends' public
interfaces.

### Consequences

* Good, because a fix to the coordinator, the follower or the store is made once and holds for both backends.
* Good, because the tests that construct `Coordinator` and `Follower` directly (for example
  `ClientRecordExpiryTests`, `CoordinatorDemotionTests`, `UnresponsiveFollowerTests`) run against the one copy.
* Bad, because it breaks the public API: `ILeaseService`, `IClientService`, `IResourceService` and their models are
  public parameters of the provider constructors, today in `Rebalancer.SqlServer.*` and `Rebalancer.Redis.*`. They
  move to one namespace, which type forwarding cannot bridge. This needs a major version (5.0.0) with a change record
  and a migration guide; hosts that only call the provider constructors with a connection string are not affected.
* Bad, because there is one more package to version and release (`release.yml` packs every project under `src/`, so
  no pipeline change).
* Bad, because a later backend-specific change to the shared roles needs an extension point instead of an edit in one
  backend.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (Decision Outcome, Consequences,
  Pros and Cons of the Options)
- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md` (ZooKeeper coordinates without a
  lease)
- `docs/adrs/0022-rebalancing-telemetry-contract.md` (the `rebalancer.backend` tag)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (Confirmation: the same change in both `Coordinator.cs`)
