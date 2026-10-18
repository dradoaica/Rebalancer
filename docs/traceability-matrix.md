# Traceability matrix

Maps architectural decision records (ADRs), user stories, primary implementation modules, and automated tests.  
Paths below are relative to the repository root: libraries under **`src/`**, samples under **`examples/`**, tests under **`tests/`**.  
Update this file when ADRs are superseded, stories change state, or new test projects are added.

| ADR | Title | User stories | Primary locations | Tests / verification | Notes |
|-----|-------|--------------|-------------------|----------------------|--------|
| [0000](adrs/0000-use-markdown-architectural-decision-records.md) | Use MADR for decision records | (process) | `docs/adrs/` | N/A | Accepted |
| [0001](adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md) | Pluggable provider and client lifecycle | US-001, US-002, US-003, US-011 | `src/Rebalancer.Core/` | `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`, `tests/Rebalancer.UnitTests` | Static `Providers` + `RebalancerClient(IRebalancerProvider)`; see [0005](adrs/0005-dependency-injection-for-provider-registration-v2.md) |
| [0002](adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) | SqlServer + Redis coordinator pattern | US-005, US-006, US-008, US-013 | `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/` | `tests/Rebalancer.IntegrationTests` (Docker), `tests/Rebalancer.UnitTests` (`RedisLeaseDecisionTests`, `CoordinatorDemotionTests`) | Assignment-safety sampling; not full ZK-style random suite; see [PRB demoted coordinator](problem-records/PRB-2026-10-18-demoted-coordinator-keeps-resources.md) |
| [0003](adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md) | ZooKeeper barriers and leader stability | US-007, US-010 | `src/Rebalancer.ZooKeeper/` | `tests/Rebalancer.IntegrationTests/ZooKeeper/` | Accepted; see [PRB ZooKeeper tests ignored their provider](problem-records/PRB-2026-10-18-zookeeper-tests-ignored-their-provider.md) |
| [0004](adrs/0004-client-options-recovery-and-assignment-delay.md) | ClientOptions (recovery, delays) | US-004, US-008 | `src/Rebalancer.Core/ClientOptions.cs` | Example app configuration | Accepted |
| [0005](adrs/0005-dependency-injection-for-provider-registration-v2.md) | DI for provider registration (v2) | US-012 | `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` | **Accepted** — dual registration with static `Providers` |
| [0006](adrs/0006-provider-resource-ownership-and-zookeeper-acl.md) | Provider ownership, Redis server-time leases, ZK ACLs | US-003, US-006, US-007 | `src/Rebalancer.Core/RebalancerClient.cs`, `src/Rebalancer.Redis/`, `src/Rebalancer.ZooKeeper/` (`Zk/ZooKeeperService.cs`, `ZooKeeperProvider.cs`), `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` (`RebalancerClientLifecycleTests`, `RedisLeaseDecisionTests`, `ZooKeeperAclApiTests`), `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs` | **Accepted** (2026-10-18); option B implemented; operator notes in `src/Rebalancer.ZooKeeper/README.md` |
| [0007](adrs/0007-non-blocking-get-assigned-resources.md) | Non-blocking `GetAssignedResources()` (SQL Server, Redis) | US-003 | `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs` | `tests/Rebalancer.UnitTests` (`GetAssignedResourcesContractTests`), `tests/Rebalancer.IntegrationTests/Redis/`, `Sql/` | **Accepted** (2026-10-18) |
| [0008](adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md) | Drop `StackExchange.Redis.DataTypes`, direct patched `Newtonsoft.Json` | US-006 | `src/Rebalancer.Redis/Rebalancer.Redis.csproj` | `tests/Rebalancer.IntegrationTests/Redis/` | **Accepted** (2026-10-18) |
| [0009](adrs/0009-reject-conflicting-provider-registrations.md) | Reject conflicting `Providers.Register` | US-001 | `src/Rebalancer.Core/Providers.cs` | `tests/Rebalancer.UnitTests` (`ProvidersRegistrationTests`) | **Accepted** (2026-10-18) |
| [0010](adrs/0010-warn-on-open-operator-znodes-when-acls-are-configured.md) | Warn on open operator znodes when ZK ACLs are configured | US-007 | `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs` | `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs` | **Accepted** (2026-10-18) |
| [0011](adrs/0011-zookeeper-acls-by-default-in-next-major-version.md) | ZooKeeper credentials required by default in 3.0 | US-007 | `src/Rebalancer.ZooKeeper/`, `src/Rebalancer.Extensions.DependencyInjection/` | To be added in the 2.x deprecation release | **Accepted** (2026-10-18); no code in 2.1.0 |

### Release 2.1.0 records

| Record | Status | Covers |
|--------|--------|--------|
| [CR-2026-10-18](change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md) | **Approved** (2026-10-18, with Amendment 1); rollback dry-run not performed; [release notes](release-notes/2.1.0.md) | ADR 0006–0009, PRB fixes; Redis all-nodes-at-once upgrade |
| [PRB demoted coordinator](problem-records/PRB-2026-10-18-demoted-coordinator-keeps-resources.md) | Root cause identified | US-005, US-006, US-013 |
| [PRB ZooKeeper tests](problem-records/PRB-2026-10-18-zookeeper-tests-ignored-their-provider.md) | Root cause identified | US-010 |

### Threat models

| Threat model | Status | Scope |
|--------------|--------|-------|
| [TM-0001](threat-models/TM-0001-zookeeper-coordination-and-acl.md) | **Accepted** (2026-10-18) | ZooKeeper coordination and ACLs |
| [TM-0002](threat-models/TM-0002-sql-server-coordination.md) | **Accepted** (2026-10-18) | SQL Server coordination (`RBR` schema) |
| [TM-0003](threat-models/TM-0003-redis-coordination.md) | **Accepted** (2026-10-18) | Redis coordination (`RBR` keys) |

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
