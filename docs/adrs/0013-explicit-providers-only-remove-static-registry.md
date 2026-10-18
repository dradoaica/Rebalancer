---
id: ADR-0013
status: Proposed
date: 2026-10-18
decides: "how a RebalancerClient obtains its provider (provider registration model)"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0001", "ADR-0005", "ADR-0009", "US-001", "US-012", "PRB-2026-10-18-zookeeper-tests-ignored-their-provider" ]
supersedes: [ "ADR-0001", "ADR-0009" ]
superseded-by: ""
---

# Explicit providers only: deprecate in 2.2, remove the static `Providers` registry in 3.0

## Context and Problem Statement

A `RebalancerClient` gets its provider in one of two ways today:

| Way                                                   | Decided in          |
|-------------------------------------------------------|---------------------|
| Static `Providers.Register(factory)` + `new RebalancerClient()` | ADR-0001, ADR-0009 |
| `new RebalancerClient(provider)` or the DI extensions | ADR-0005            |

The static registry is process-wide state. It caused a real defect: the ZooKeeper integration tests ran every test with
the first test's provider (PRB-2026-10-18-zookeeper-tests-ignored-their-provider). ADR-0009 turned that silent failure
into an exception, but the global state remains. Two accepted records also now disagree: ADR-0001 says a duplicate
registration "is silently ignored", ADR-0009 says a conflicting one throws. This record decides the registration model
for 3.0 and resolves that contradiction.

## Decision Drivers

* No process-wide mutable state in the client API: parallel tests and several resource groups with different backends
  in one process must just work.
* One supported way to supply a provider, already in place since 2.0: explicit or DI (ADR-0005).
* Users get a compiler warning before anything is removed (same pattern as ADR-0011).

## Considered Options

* **A. Deprecate in 2.2.0, remove in 3.0.** Mark `Providers` and the parameterless `RebalancerClient()` constructor
  `[Obsolete]` in 2.2.0, with a message pointing to `new RebalancerClient(provider)` and the DI extensions, then remove
  both in 3.0.
* **B. Keep the registry (ADR-0009 as is).**
* **C. Keep it, `[Obsolete]` permanently,** never remove.

## Decision Outcome

Proposed option: **A**. The strongest argument against A is that the registry is the simplest way to get started, and
removing it breaks every caller that uses it, including the repository's own example application. The replacement is
one line (`new RebalancerClient(new SqlServerProvider(...))`), the DI path already exists, and the registry is the
documented root cause of a defect in this repository. C keeps the global state forever for no benefit.

What ADR-0001 decided otherwise stays unchanged and is restated here so ADR-0001 can be superseded as a whole:
`IRebalancerProvider` is the backend abstraction, `RebalancerClient` owns the public API and events
(`OnAssignment`, `OnUnassignment`, `OnAborted`), and providers are pluggable per backend package. ADR-0009 becomes moot
in 3.0, because the method it governs no longer exists. Until then (2.2.x) its behavior stays.

### Consequences

* Good, because client construction has no hidden global state; ADR-0001's and ADR-0009's drawbacks disappear.
* Good, because the ADR-0001 / ADR-0009 contradiction is resolved by superseding both.
* Bad, because 3.0 breaks every caller of `Providers.Register` and `new RebalancerClient()`. The migration guide and
  the 3.0 change record must cover it, and the example application must move to an explicit provider.
* Bad, because US-001 ("register a provider factory") describes the removed path and needs a successor story for 3.0.

### Confirmation

* 2.2.0: unit test that `Providers` and `RebalancerClient()` carry `[Obsolete]`; the example builds with warnings
  as errors only after moving to an explicit provider.
* 3.0: `Providers` and the parameterless constructor no longer exist (reflection test); `ProvidersRegistrationTests`
  are removed with the type they test.

## More Information

* Supersedes [ADR-0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md) and
  [ADR-0009](0009-reject-conflicting-provider-registrations.md); builds on
  [ADR-0005](0005-dependency-injection-for-provider-registration-v2.md) (whose "keep `Providers` in this milestone"
  applied to v2). On acceptance, a human sets ADR-0001 and ADR-0009 to `Superseded` with `superseded-by: ADR-0013`.
* Releases: 2.2.0 (deprecation), 3.0.0 (removal).
