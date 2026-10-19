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
- Tests: **`tests/Rebalancer.IntegrationTests`** — **ZooKeeper** barrier/randomized suite under `ZooKeeper/` (requires live ZK); **SqlServer + Redis** assignment-safety sampling with Testcontainers (**Docker**). Since 3.2.0 also paused-node scenarios (a follower paused past its keep-alive expiry, a coordinator stalled past its lease expiry), the SQL Server least-privilege role, and (4.0) the Redis per-group key move. **`tests/Rebalancer.UnitTests`** — DI registration, provider lifecycle, 3.0 API contract, ZooKeeper ACL API, Redis lease decisions, record expiry, coordinator demotion and unresponsive followers; no Docker — [US-010](../user-stories/US-010-zookeeper-tests-uphold-assignment-safety.md), [US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md), [US-013](../user-stories/US-013-cross-backend-assignment-safety-tests.md).

## Out of scope (explicit non-goals for this repo today)

- Other language ports (README describes a future multi-language suite; this repo is .NET).
- etcd / Consul backends (mentioned in product vision, not implemented here).
- Guaranteed exactly-once message processing (hosts must design idempotency).
- Production RabbitMQ or SQL topology in the sample projects (samples are illustrative).

## Versioning and compatibility

- **SemVer** for NuGet packages when published; breaking public API or behavior changes require a **major** bump and, per governance, a **superseding ADR** or new ADR plus change record.
- [ADR 0005](../adrs/0005-dependency-injection-for-provider-registration-v2.md) is **accepted**; `Rebalancer.Extensions.DependencyInjection` is the recommended host registration path ([US-012](../user-stories/US-012-register-rebalancer-with-dependency-injection.md)). The static `Providers` registry was removed in 3.0; without DI, pass the provider to `RebalancerClient` ([ADR-0013](../adrs/0013-explicit-providers-only-remove-static-registry.md), [US-0015](../user-stories/US-0015-supply-provider-explicitly-to-rebalancer-client.md)).
- Current release: **4.1.0** (metrics, traces, health check; additive). Previous: **4.0.0** (breaking for Redis provisioning only: per-group keys, [ADR-0020](../adrs/0020-per-group-redis-leases-and-resources.md), [migration guide](../release-notes/migrating-to-4.0.md)). Release history and change records: [`docs/release-notes/`](../release-notes/), [`docs/change-records/`](../change-records/).

## Security baseline

- Connection strings, Redis, ZooKeeper, and RabbitMQ credentials are **host-supplied**; use secret stores and least-privilege identities.
- ZooKeeper paths may hold assignment metadata — use **ACLs** and private networks. Credentials or an explicit ACL are required since 3.0 (ADR-0011); operator-created znodes: runbook in `src/Rebalancer.ZooKeeper/README.md`.
- SQL Server: role `rebalancer_node` in `db/RebalanserLeastPrivilege.sql`. Redis 4.0: one ACL user per group, no shared keys.
- Threat models: [`docs/threat-models/`](../threat-models/) (TM-0001 ZooKeeper, TM-0002 SQL Server, TM-0003 Redis).

## Roadmap themes (v2)

1. **DI-first registration** — shipped: ADR 0005, US-012, `Rebalancer.Extensions.DependencyInjection`.  
2. **Assignment-safety tests for SqlServer + Redis** — shipped: US-013, `tests/Rebalancer.IntegrationTests` (Docker).  
3. **4.1.0** — metrics and traces (ADR-0022), health check package (ADR-0023), randomised SQL Server / Redis tests.
4. **Next** — CI matrix with Docker (backlog). Formerly listed here: OpenTelemetry ([US-0017](../user-stories/US-0017-emit-rebalancing-metrics-and-traces.md)), health checks ([US-0018](../user-stories/US-0018-report-client-health-to-the-host.md)), CI matrix with Docker, deeper randomized parity with ZK tests ([US-0019](../user-stories/US-0019-randomised-assignment-safety-tests-for-sql-server-and-redis.md)) (implemented in 4.1.0).
