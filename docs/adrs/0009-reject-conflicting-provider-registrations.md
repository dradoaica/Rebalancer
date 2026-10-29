---
id: ADR-0009
status: Superseded
date: 2026-10-18
decides: "handling of a conflicting second Providers.Register call"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0001", "ADR-0005", "US-0001", "PRB-0002" ]
supersedes: [ ]
superseded-by: "ADR-0013"
---

# Reject a conflicting second `Providers.Register` call instead of ignoring it

## Context and Problem Statement

`Providers.Register` is static and keeps only the first factory. Every later call is ignored without any signal. ADR
0001 recorded this as a drawback ("can surprise tests or mis-ordered startup"). On 2026-10-18 it did exactly that: every
ZooKeeper integration test registered its own factory, but all of them ran with the first test's provider. The
ResourceBarrier mode was not exercised at all, and the suite still passed (`docs/problem-records/PRB-0002-zookeeper-tests-ignored-their-provider.md`).

The tests now pass providers explicitly, but the trap remains for applications: a second, different registration is
silently lost. ADR 0005 keeps `Providers` (not `[Obsolete]`) in this milestone, so the fix has to stay compatible with
its intended use: register once at startup, then create clients with `new RebalancerClient()`.

## Decision Drivers

* A registration that does not take effect must not fail silently.
* Keep the intended use (register once) working unchanged, binary-compatible, and without new public API if possible.
* Point users to the DI / explicit-provider path (ADR 0005), which has no global state.

## Considered Options

* **A. Throw `ProviderException` when a second registration uses a different factory.** Registering the same factory
  again (`Delegate.Equals`: same method and target) stays a no-op. The message names the explicit alternatives:
  `new RebalancerClient(provider)` or the DI extensions.
* **B. Last registration wins.** This silently changes which provider earlier code gets, which is worse than today.
* **C. Write a warning to `System.Diagnostics.Trace`** and keep first-wins. Core has no logger at that point, so the
  warning is invisible in most hosts.
* **D. Throw on any second call,** even with the same factory.
* **E. Keep first-wins; add `bool Providers.TryRegister(...)`.** New API, and plain `Register` stays silent.

## Decision Outcome

Chosen option: **A**. The strongest argument against A is that it turns a silent no-op into an exception at startup.
A host that re-runs its startup code in one process will hit it. So will a test suite that registers per test. Re-running
with a capturing lambda counts as well: a new closure is a different delegate, so even registering "the same" lambda
again would throw. All of those callers are already getting a provider other than the one they asked for, without
knowing it. An exception at registration is the cheapest place to find out. D is stricter than needed and would break
idempotent startup code. E leaves the trap in place.

### Consequences

* Good, because a lost registration becomes a clear, immediate error instead of a wrong provider at runtime.
* Good, because the intended single registration is unchanged, and no public signature changes.
* Bad, because callers that register twice with different factories (often by accident) now fail at startup. Release
  notes must say so and point to `new RebalancerClient(provider)` / DI.

### Confirmation

* Unit tests in `tests/Rebalancer.UnitTests`: registering the same factory twice is a no-op; registering a different
  factory throws `ProviderException`; `GetProvider()` still returns the first factory's provider. Static state needs a
  test-only reset, e.g., through `InternalsVisibleTo`. That is internal, not public API.
* The example application (single registration) still starts.

## More Information

* Related: [ADR 0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md) (drawback recorded),
  [ADR 0005](0005-dependency-injection-for-provider-registration-v2.md) (`Providers` kept in this milestone), US-0001.

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md` (ADR-0001: Pluggable Rebalancer provider and
  client lifecycle)
- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md` (ADR-0005: Dependency injection for provider
  registration (v2))
- `docs/user-stories/US-0001-register-provider-and-join-resource-group.md` (US-0001: Register backend provider and join
  a resource group)
- `docs/problem-records/PRB-0002-zookeeper-tests-ignored-their-provider.md` (PRB-0002: ZooKeeper integration tests ran
  with the first test's provider)
