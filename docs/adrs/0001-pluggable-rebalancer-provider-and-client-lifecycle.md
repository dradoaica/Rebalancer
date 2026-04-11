---
status: accepted
date: 2026-04-11
decision-makers: []
consulted: []
informed: []
---

# Pluggable Rebalancer provider and client lifecycle

## Context and Problem Statement

Rebalancer must support multiple coordination backends (SQL Server, Redis, ZooKeeper) without forcing host applications to depend on all of them at once. The public entry point for integrators is `RebalancerClient`, which must delegate coordination to a backend-specific implementation while exposing a stable lifecycle and event model.

This decision records the **as-built** design in `src/Rebalancer.Core/`.

## Decision Drivers

* Swappable storage/coordination technology per deployment.
* Single public client type (`RebalancerClient`) for application code.
* Minimal coupling between host startup and provider construction (no mandatory DI container).
* Observable behavior: assignment changes, unassignment, and unrecoverable failures.

## Considered Options

* **Dependency injection only** — Host receives `IRebalancerProvider` via constructor/factory; no static registry.
* **Static provider registry (chosen as-built)** — `Providers.Register(Func<IRebalancerProvider>)` called once before `RebalancerClient` use; `RebalancerClient` resolves the provider via `Providers.GetProvider()`.
* **Single assembly with all backends** — One package always references every backend (rejected by modular project layout).

## Decision Outcome

Chosen option: **Static provider registry** with **`IRebalancerProvider`** implementing coordination, and **`RebalancerClient`** owning the outward-facing API and events.

### Consequences

* Good, because applications depend only on `Rebalancer.Core` plus the one backend package they reference.
* Good, because `IRebalancerProvider` isolates backend-specific protocols from `RebalancerClient` event wiring.
* Bad, because `Providers.Register` accepts only the first registration; duplicate registration is silently ignored, which can surprise tests or mis-ordered startup.
* Bad, because static state complicates parallel tests or multiple independent resource groups with different provider types in one process (not addressed in current API).

### Confirmation

* **API review:** `IRebalancerProvider` in `src/Rebalancer.Core/IRebalancerProvider.cs` defines `StartAsync`, `RecreateClientAsync`, `WaitForCompletionAsync`, `GetAssignedResources`, `GetState`.
* **Lifecycle:** `RebalancerClient` in `src/Rebalancer.Core/RebalancerClient.cs` registers `OnChangeActions` with the provider, exposes `OnAssignment` / `OnUnassignment` / `OnAborted`, and cancels via `CancellationTokenSource` on stop/dispose.
* **Example:** `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs` calls `Providers.Register(() => new SqlServerProvider(...))` then `StartAsync`.

### Logging (subsection)

Structured logging uses **`IRebalancerLogger`** (`src/Rebalancer.Core/Logging/IRebalancerLogger.cs`) with implementations such as null, console, and Microsoft extensions. Providers accept a logger in their constructors where applicable. This keeps observability optional and testable without mandating a logging framework in Core.

## Pros and Cons of the Options

### Static provider registry

* Good, because startup remains a few lines without a DI bootstrapper.
* Bad, because global registration is a hidden dependency and is not thread-safe for multiple re-registrations by design.

### Dependency injection only

* Good, because explicit composition and test doubles avoid static state.
* Bad, because it was not the as-built approach; migrating would be a breaking API change.

## More Information

* Provider implementers wire user callbacks through `OnChangeActions` (`src/Rebalancer.Core/OnChangeActions.cs`).
* If the registry is never populated, `Providers.GetProvider()` throws `ProviderException` (`src/Rebalancer.Core/Providers.cs`).
