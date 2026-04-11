# Rebalancer — product baseline (.NET)

**Status:** living document — v1 as-built plus v2 planning pointers.  
**See also:** [traceability matrix](../traceability-matrix.md), [ADRs](../adrs/), [user stories](../user-stories/).

## Repository layout

| Area | Path |
|------|------|
| Libraries | `src/Rebalancer.*` (Core, SqlServer, Redis, ZooKeeper, Extensions.DependencyInjection) |
| Examples | `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`, `examples/Rebalancer.RabbitMq.Tools` |
| Tests | `tests/Rebalancer.UnitTests`, `tests/Rebalancer.IntegrationTests` |

## Product intent

Rebalancer is a **.NET library** for **Kafka-style consumer groups** over arbitrary resource identifiers (queues, files, buckets, etc.): a **coordinator** assigns resources to **participating nodes**; nodes react via callbacks when assignments change. Coordination state lives in a pluggable **backend** (SQL Server, Redis, or ZooKeeper in this repository).

## In scope (current repository)

- `Rebalancer.Core` — public `RebalancerClient`, `IRebalancerProvider`, `ClientOptions`, logging abstraction.
- `Rebalancer.SqlServer`, `Rebalancer.Redis` — coordinator/follower + lease + store pattern.
- `Rebalancer.ZooKeeper` — leader election, `ResourceBarrier` / `GlobalBarrier` modes.
- Example: RabbitMQ + SQL Server sample (`examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`).
- Tooling: RabbitMQ CLI (`examples/Rebalancer.RabbitMq.Tools`).
- Tests: **`tests/Rebalancer.IntegrationTests`** — **ZooKeeper** barrier/randomized suite under `ZooKeeper/` (requires live ZK); **SqlServer + Redis** assignment-safety sampling with Testcontainers (**Docker**). **`tests/Rebalancer.UnitTests`** — DI registration tests — [US-010](../user-stories/US-010-zookeeper-tests-uphold-assignment-safety.md), [US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md), [US-013](../user-stories/US-013-cross-backend-assignment-safety-tests.md).

## Out of scope (explicit non-goals for this repo today)

- Other language ports (README describes a future multi-language suite; this repo is .NET).
- Etcd / Consul backends (mentioned in product vision, not implemented here).
- Guaranteed exactly-once message processing (hosts must design idempotency).
- Production RabbitMQ or SQL topology in the sample projects (samples are illustrative).

## Versioning and compatibility

- **SemVer** for NuGet packages when published; breaking public API or behavior changes require a **major** bump and, per governance, a **superseding ADR** or new ADR plus change record.
- [ADR 0005](../adrs/0005-dependency-injection-for-provider-registration-v2.md) is **accepted**; `Rebalancer.Extensions.DependencyInjection` is the recommended host registration path alongside static `Providers` ([US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md)).

## Security baseline

- Connection strings, Redis, ZooKeeper, and RabbitMQ credentials are **host-supplied**; use secret stores and least-privilege identities.
- ZooKeeper paths may hold assignment metadata — use **ACLs** and private networks.

## Roadmap themes (v2)

1. **DI-first registration** — shipped: ADR 0005, US-012, `Rebalancer.Extensions.DependencyInjection`.  
2. **Assignment-safety tests for SqlServer + Redis** — shipped: US-013, `tests/Rebalancer.IntegrationTests` (Docker).  
3. **Next** — OpenTelemetry, health checks, CI matrix with Docker, deeper randomized parity with ZK tests (backlog).
