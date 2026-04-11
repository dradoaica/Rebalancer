---
status: accepted
date: 2026-04-11
decision-makers: []
consulted: []
informed: []
---

# ZooKeeper backend: leader election, barrier algorithms, and rebalancing modes

## Context and Problem Statement

The **ZooKeeper** backend implements coordination using znodes, watches, and ephemeral nodes. Rebalancing must not assign the same resource to two clients simultaneously; two algorithms—**Resource Barrier** and **Global Barrier**—trade off RPC count and complexity. Leader stability must handle session loss, epoch/version conflicts, and concurrent coordinator attempts.

This decision captures the **as-built** approach documented in detail in [`src/Rebalancer.ZooKeeper/README.md`](../../src/Rebalancer.ZooKeeper/README.md) and implemented in `ZooKeeperProvider` and related types.

## Decision Drivers

* Strong coordination primitives from ZooKeeper (sequential ephemeral nodes, watches, versioned `setData`).
* Configurable rebalancing strategy via `RebalancingMode` (`ResourceBarrier` vs `GlobalBarrier`).
* Mitigation of “split brain” coordinators via epoch znode and version checks.
* Rate limiting of rebalancing churn via minimum interval between rebalancing attempts.

## Considered Options

* **ResourceBarrier only** — Per-resource barrier znodes; fewer global phases.
* **GlobalBarrier only** — Single stop/start choreography; fewer RPCs when resource count is large.
* **Both modes selectable (as-built)** — `RebalancingMode` enum in `src/Rebalancer.ZooKeeper/RebalancingMode.cs`, chosen when constructing `ZooKeeperProvider`.

## Decision Outcome

Chosen option: **Expose both `ResourceBarrier` and `GlobalBarrier`** on the ZooKeeper provider, with leader election under the resource group’s client path and additional mechanisms (epoch, versioned writes) to detect zombie coordinators.

### Consequences

* Good, because operators can pick the algorithm that matches scale (many resources vs few) and operational tolerance for global stop phases.
* Good, because extensive README diagrams and step lists align with test scenarios.
* Bad, because ZooKeeper protocol surface area is large; operational mistakes (ACLs, path layout) are outside the library.
* Bad, because the ZK README states the protocol is still under development—callers should treat behavior as version-coupled to this codebase.

### Confirmation

* **Implementation:** `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs` switches on `RebalancingMode` for barrier behavior.
* **Resource management:** `src/Rebalancer.ZooKeeper/ResourceManagement/ResourceManager.cs` encodes mode-specific logic.
* **Tests:** `tests/Rebalancer.IntegrationTests/ZooKeeper/` includes single-client, multi-client, and randomized barrier tests (e.g. `RandomisedResourceBarrierTests.cs`) validating core invariants such as no double assignment under tested scenarios.

## Pros and Cons of the Options

### ResourceBarrier

* Good, because localized barriers can be simpler to reason about per resource.
* Bad, because RPC count scales with resources.

### GlobalBarrier

* Good, because RPC profile can be favorable at high resource counts.
* Bad, because global stop/start phases add latency and complexity.

## More Information

* Invariants stated in `src/Rebalancer.ZooKeeper/README.md` (e.g. never assign one resource to two nodes) guide test intent; formal TLA+ proofs referenced in the product README are out of scope for this ADR.
