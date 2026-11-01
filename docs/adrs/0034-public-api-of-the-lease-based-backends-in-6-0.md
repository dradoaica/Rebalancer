---
id: ADR-0034
status: Proposed
date: 2026-11-01
decides: "public API of Rebalancer.LeaseCoordination and the lease-based backends beyond the providers (6.0)"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0013", "ADR-0014", "ADR-0029" ]
supersedes: [ ]
superseded-by: ""
---

# Public API of the lease-based backends in 6.0

## Context and Problem Statement

ADR-0029 made the service interfaces and their models the public surface of `Rebalancer.LeaseCoordination`, because
the provider constructors accept custom `ILeaseService`, `IClientService` and `IResourceService` implementations. A
review after 5.0.0 (CR-0013, Post-implementation) found two problems with that surface:

* Types that no public signature uses are public: `ClientEvent`, `CoordinatorToken`, `EventType` and
  `AssignmentStatus` in `Rebalancer.LeaseCoordination` (new in 5.0), and, already before 5.0, `ResourceGroup`,
  `Resources.Resource` and `Utils.Constants` in `Rebalancer.Redis` and `Connections.ConnectionHelper` in
  `Rebalancer.SqlServer`. The PostgreSQL equivalents are internal.
* A custom `IClientService` cannot take part in ADR-0014's re-registration: the coordinator and follower catch only
  `ClientNotFoundException`, which is internal, so a custom service has no way to say "this record is gone".

This record decides the public API of these packages, apart from the providers, for the next major version.

## Considered Options

* **A. Deprecate in a 5.x minor, change in 6.0:** mark the unused public types `[Obsolete]` in 5.x, make them internal
  in 6.0; make `ClientNotFoundException` public in 5.x (additive).
* **B. Change everything in 6.0 only**, without a deprecation release.
* **C. Keep the surface as it is**, and document that these types are not part of the supported API.

## Decision Outcome

Chosen option: "A. Deprecate in a 5.x minor, change in 6.0", because it is how this repository removed public API
before (ADR-0013: deprecated in 2.2.0, removed in 3.0), it gives users one release of compiler warnings before the
break, and the one additive change (`ClientNotFoundException`) needs no major version.

* 5.x minor: `[Obsolete]` on the eight types above, with a message naming 6.0; `ClientNotFoundException` public, with
  its use documented on `IClientService.KeepAliveAsync`.
* 6.0: the eight types internal (the test projects keep `InternalsVisibleTo`).
* Each release gets a change record, and 6.0 a migration guide entry.

### Consequences

* Good, because the supported surface is what the provider constructors need, and custom services can re-register.
* Good, because users see the change one release ahead.
* Bad, because a host that used one of these types (none in this repository's examples or extensions) must change.
* Bad, because it ties 6.0 to this change, whatever else 6.0 contains.

## Sources

- `docs/adrs/0013-explicit-providers-only-remove-static-registry.md` (option A: deprecate, then remove)
- `docs/adrs/0014-expire-stale-client-records.md` (re-registration of a client whose record is gone)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (the public service types)
