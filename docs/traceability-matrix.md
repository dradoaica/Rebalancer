# Traceability matrix

Maps architectural decision records (ADRs), user stories, primary implementation modules, and automated tests.  
Paths below are relative to the repository root: libraries under **`src/`**, samples under **`examples/`**, tests under **`tests/`**.  
Update this file when ADRs are superseded, stories change state, or new test projects are added.

| ADR | Title | User stories | Primary locations | Tests / verification | Notes |
|-----|-------|--------------|-------------------|----------------------|--------|
| [0000](adrs/0000-use-markdown-architectural-decision-records.md) | Use MADR for decision records | (process) | `docs/adrs/` | N/A | Accepted |
| [0001](adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md) | Pluggable provider and client lifecycle | US-001, US-002, US-003, US-011 | `src/Rebalancer.Core/` | `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`, `tests/Rebalancer.UnitTests` | Static `Providers` + `RebalancerClient(IRebalancerProvider)`; see [0005](adrs/0005-dependency-injection-for-provider-registration-v2.md) |
| [0002](adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) | SqlServer + Redis coordinator pattern | US-005, US-006, US-008, US-013 | `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/` | `tests/Rebalancer.IntegrationTests` (Docker) | Assignment-safety sampling; not full ZK-style random suite |
| [0003](adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md) | ZooKeeper barriers and leader stability | US-007, US-010 | `src/Rebalancer.ZooKeeper/` | `tests/Rebalancer.IntegrationTests/ZooKeeper/` | Accepted |
| [0004](adrs/0004-client-options-recovery-and-assignment-delay.md) | ClientOptions (recovery, delays) | US-004, US-008 | `src/Rebalancer.Core/ClientOptions.cs` | Example app configuration | Accepted |
| [0005](adrs/0005-dependency-injection-for-provider-registration-v2.md) | DI for provider registration (v2) | US-012 | `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` | **Accepted** — dual registration with static `Providers` |

### Stories without a single ADR (tooling / quality)

| Story | Title | Links | Locations |
|-------|-------|-------|-----------|
| [US-009](user-stories/US-009-rabbitmq-tools-cli-for-queue-setup.md) | RabbitMQ tools CLI | README | `examples/Rebalancer.RabbitMq.Tools/` |

### v2 documentation slice

| Story | Title | Status | Outcome |
|-------|-------|--------|---------|
| [US-014](user-stories/US-014-traceability-matrix-and-product-baseline.md) | Matrix + product baseline | Done | [rebalancer-product-baseline.md](initial-product-specs/rebalancer-product-baseline.md), this matrix, [architecture overview](architecture/rebalancer-overview.md), README **Documentation** section |

---

**Maintenance:** At release boundaries, align story **Done** states and add a change record reference in `docs/change-records/` if your process requires it.
