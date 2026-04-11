---
status: accepted
date: 2026-04-11
decision-makers: []
consulted: []
informed: []
---

# Coordinator, follower, lease, and resource store pattern for SQL Server and Redis backends

## Context and Problem Statement

Two backends—**SQL Server** and **Redis**—implement the same high-level Rebalancer idea: one **coordinator** node assigns resource identifiers to nodes, and **followers** follow those assignments, using a shared metadata store. Both need mutual exclusion when updating assignments and a way to detect liveness.

This ADR documents the **shared structural pattern** in `src/Rebalancer.SqlServer/` and `src/Rebalancer.Redis/` as implemented, not a formal proof of correctness.

## Decision Drivers

* Reuse the same conceptual model (coordinator vs follower) across RDBMS and Redis deployments.
* Serialize concurrent updates safely (leases + store operations).
* Keep a per–resource-group store of clients, resources, and assignment state suitable for periodic rebalancing.

## Considered Options

* **Single generic implementation** parameterized by `IDatabase` / ADO.NET abstractions.
* **Parallel implementations per backend (as-built)** — Separate `SqlServerProvider` and `RedisProvider` with mirrored folders: `Clients`, `Leases`, `Resources`, `Roles`, `Store`.
* **ZooKeeper-only** for all environments — rejected for deployments that already standardize on SQL Server or Redis.

## Decision Outcome

Chosen option: **Parallel implementations** with the same **lease service**, **resource group store**, **coordinator/follower roles**, and **client service** concepts, differing only in data access (ADO.NET vs StackExchange.Redis).

### Consequences

* Good, because each backend can use native concurrency and connection patterns (e.g., transactions vs Redis primitives).
* Good, because operators can pick SQL Server where they already have HA and backups, or Redis where low-latency in-memory coordination fits.
* Bad, because behavior and bug fixes may need to be applied in two places; drift risk is mitigated only by discipline and tests.
* Bad, because README-described “global barrier” semantics for RDBMS are implemented inside these providers’ protocols; readers must consult code for exact ordering guarantees.

### Confirmation

* **Structural parity:** Compare `src/Rebalancer.SqlServer/SqlServerProvider.cs` and `src/Rebalancer.Redis/RedisProvider.cs` for coordinator/follower orchestration.
* **Leases:** `src/Rebalancer.SqlServer/Leases/LeaseService.cs` and `src/Rebalancer.Redis/Leases/LeaseService.cs` implement acquire/renew/relinquish patterns.
* **Store:** `ResourceGroupStore` in each backend under `Store/` (within those projects).
* **Usage path:** Example host uses SQL Server provider in `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`.

## Pros and Cons of the Options

### Parallel SqlServer and Redis implementations

* Good, because clear mapping from documentation (“Coordinator assigns resources”) to code modules.
* Bad, because duplicated logic increases maintenance cost.

### Single abstraction over both stores

* Good, because one place to fix algorithmic issues.
* Bad, because SQL and Redis semantics differ; a leaky abstraction could hide subtle bugs (not chosen as-built).

## More Information

* Product-level description of resource groups and barriers appears in the repository [`README.md`](../../README.md).
* Redis uses `StackExchange.Redis` (`ConnectionMultiplexer`) as seen in `RedisProvider` constructor.
