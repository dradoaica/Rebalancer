---
status: accepted
date: 2026-04-11
decision-makers: []
consulted: []
informed: []
---

# Dependency injection for provider registration (v2)

## Context and Problem Statement

The as-built API uses a static `Providers.Register` singleton ([ADR 0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md)). That is simple for demos but complicates **unit testing** (ordering, parallel tests), **multiple resource groups with different backends in one process**, and alignment with **ASP.NET Core / Generic Host** patterns where `IRebalancerProvider` should be resolved from a container with explicit lifetime and configuration.

This ADR records the **accepted v2 approach**: first-class registration via **dependency injection** alongside static `Providers` for backward compatibility.

## Decision Drivers

* Testability without global mutable state.
* Explicit lifetimes (singleton vs scoped) and named options per backend.
* Consistency with `Microsoft.Extensions.DependencyInjection` in typical .NET hosts.
* Non-breaking migration path from static `Providers` for current integrators.

## Considered Options

* **Status quo** — Only static `Providers`; document workarounds for tests (AppDomains / process isolation).
* **DI-only breaking change** — Remove `Providers` in v2; all hosts must use DI.
* **Dual registration (recommended)** — Keep `Providers` as a thin adapter over `Func<IRebalancerProvider>` or `IServiceProvider`, and add `IServiceCollection` extension methods (`AddRebalancerSqlServer`, etc.) that register factory + options; `RebalancerClient` gains an optional constructor `RebalancerClient(IRebalancerProvider)` or resolves from `IServiceProvider` when configured.
* **Factory interface only** — Introduce `IRebalancerProviderFactory` without full DI package dependency in Core (minimal coupling).

## Decision Outcome

Chosen option: **Dual registration** — **DI extensions** live in `Rebalancer.Extensions.DependencyInjection` (`src/Rebalancer.Extensions.DependencyInjection/`). Static `Providers.Register` remains supported for existing hosts. `RebalancerClient` accepts an injected `IRebalancerProvider` via constructor.

### Consequences

* Good, because hosts can follow standard .NET patterns and tests can substitute providers without static state.
* Good, because existing samples can migrate incrementally.
* Bad, because maintaining two paths increases API surface until `Providers` is removed or relegated to obsolete.
* Bad, because `RebalancerClient` may need constructor overloads or a small builder type—design must avoid ambiguity with two entry points.

### Confirmation

* Compliance: `tests/Rebalancer.UnitTests` (and future integration coverage) resolve `RebalancerClient` or `IRebalancerProvider` from a test `ServiceProvider` without calling `Providers.Register`.
* Documentation: README and [docs/traceability-matrix.md](../traceability-matrix.md) updated when implementation ships.
* Link to implementation story: [US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md).

## Pros and Cons of the Options

### Dual registration

* Good, balances migration and modern hosting.
* Bad, temporary complexity in docs and code paths.

### DI-only breaking change

* Good, single clear story for maintainers.
* Bad, churn for all existing integrators without a phased deprecation.

## More Information

* Does not supersede [0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md); both registration styles coexist until a future ADR deprecates static `Providers`.
* Implementation: [US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md).
