---
status: accepted
date: 2026-04-11
decision-makers: []
consulted: []
informed: []
---

# Client options: automatic recovery, restart delay, and assignment delay

## Context and Problem Statement

Nodes participating in a resource group may hit transient failures or messaging-broker quirks (e.g. connection keep-alives vs reassignment speed). The library exposes **`ClientOptions`** so hosts can tune recovery and optionally delay assignment notifications to avoid duplicate or out-of-order consumption.

This ADR documents the **as-built** semantics in `src/Rebalancer.Core/ClientOptions.cs` and how providers consume those options.

## Decision Drivers

* Recover from non-fatal errors without manual process restarts where possible (`AutoRecoveryOnError`, `RestartDelay`).
* Avoid message ordering issues when brokers retain dead connections briefly (`OnAssignmentDelay`—documented for RabbitMQ-style scenarios in XML comments).
* Keep options serializable POCOs on the public API surface.

## Considered Options

* **Fixed delays hard-coded** in providers — simple but inflexible.
* **Configurable `ClientOptions` (as-built)** — set per `StartAsync` call on `RebalancerClient`.
* **Per-resource broker policies** — richer but not present as-built.

## Decision Outcome

Chosen option: **`ClientOptions`** with `AutoRecoveryOnError`, `RestartDelay`, and `OnAssignmentDelay` passed into `IRebalancerProvider.StartAsync` via `RebalancerClient.StartAsync`.

### Consequences

* Good, because integrators can align assignment delay with broker TCP/RabbitMQ heartbeat timeouts (see XML documentation on `OnAssignmentDelay`).
* Good, because auto-recovery behavior is explicit in configuration rather than implicit retries only inside providers.
* Bad, because incorrect tuning can still lead to duplicate processing or long stalls; hosts remain responsible for idempotency and monitoring.
* Bad, because options alone do not guarantee exactly-once semantics across all backends.

### Confirmation

* **Source of truth:** `src/Rebalancer.Core/ClientOptions.cs` XML comments describe intent and RabbitMQ example rationale.
* **Propagation:** `RebalancerClient.StartAsync(string, ClientOptions)` passes `clientOptions` to `rebalancerProvider.StartAsync` (`src/Rebalancer.Core/RebalancerClient.cs`).
* **Example:** `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs` sets `AutoRecoveryOnError` and `RestartDelay` for the sample consumer.

## Pros and Cons of the Options

### Configurable ClientOptions

* Good, because one type centralizes operational tuning for all backends that honor it.
* Bad, because not every sub-scenario may interpret every flag identically; integrators should verify against their backend.

## More Information

* High-level product goals appear in [`README.md`](../../README.md) (RabbitMQ consumer groups, technology-agnostic resources).
