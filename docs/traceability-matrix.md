# Traceability matrix

Maps architectural decision records (ADRs), user stories, primary implementation modules, and automated tests.  
Paths below are relative to the repository root: libraries under **`src/`**, samples under **`examples/`**, tests under **`tests/`**.  
Update this file when ADRs are superseded, stories change state, or new test projects are added.

| ADR | Title | User stories | Primary locations | Tests / verification | Notes |
|-----|-------|--------------|-------------------|----------------------|--------|
| [0000](adrs/0000-use-markdown-architectural-decision-records.md) | Use MADR for decision records | (process) | `docs/adrs/` | N/A | Accepted |
| [0001](adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md) | Pluggable provider and client lifecycle | US-001, US-002, US-003, US-011 | `src/Rebalancer.Core/` | `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`, `tests/Rebalancer.UnitTests` | **Superseded** by [0013](adrs/0013-explicit-providers-only-remove-static-registry.md) (static `Providers` removed in 3.0) |
| [0002](adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) | SqlServer + Redis coordinator pattern | US-005, US-006, US-008, US-013 | `src/Rebalancer.SqlServer/`, `src/Rebalancer.Redis/` | `tests/Rebalancer.IntegrationTests` (Docker: `*AssignmentSafetyTests`, `*PausedNodeTests`), `tests/Rebalancer.UnitTests` (`RedisLeaseDecisionTests`, `CoordinatorDemotionTests`) | Assignment-safety sampling; not full ZK-style random suite; see [PRB-0001](problem-records/PRB-0001-demoted-coordinator-keeps-resources.md) |
| [0003](adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md) | ZooKeeper barriers and leader stability | US-007, US-010 | `src/Rebalancer.ZooKeeper/` | `tests/Rebalancer.IntegrationTests/ZooKeeper/` | Accepted; see [PRB-0002](problem-records/PRB-0002-zookeeper-tests-ignored-their-provider.md) |
| [0004](adrs/0004-client-options-recovery-and-assignment-delay.md) | ClientOptions (recovery, delays) | US-004, US-008 | `src/Rebalancer.Core/ClientOptions.cs` | Example app configuration | Accepted |
| [0005](adrs/0005-dependency-injection-for-provider-registration-v2.md) | DI for provider registration (v2) | US-012 | `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` (`DiRegistrationTests`) | **Accepted**; the dual registration with static `Providers` ended in 3.0 ([0013](adrs/0013-explicit-providers-only-remove-static-registry.md)) |
| [0006](adrs/0006-provider-resource-ownership-and-zookeeper-acl.md) | Provider ownership, Redis server-time leases, ZK ACLs | US-003, US-006, US-007 | see 0016, 0017, 0018 | see 0016, 0017, 0018 | **Superseded** by [0016](adrs/0016-rebalancer-client-owns-its-provider.md) (and split into 0017, 0018) |
| [0007](adrs/0007-non-blocking-get-assigned-resources.md) | Non-blocking `GetAssignedResources()` (SQL Server, Redis) | US-003 | `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs` | `tests/Rebalancer.UnitTests` (`GetAssignedResourcesContractTests`), `tests/Rebalancer.IntegrationTests/Redis/`, `Sql/` | **Accepted** (2026-10-18) |
| [0008](adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md) | Drop `StackExchange.Redis.DataTypes`, direct patched `Newtonsoft.Json` | US-006 | `src/Rebalancer.Redis/Rebalancer.Redis.csproj` | `tests/Rebalancer.IntegrationTests/Redis/` | **Accepted** (2026-10-18) |
| [0009](adrs/0009-reject-conflicting-provider-registrations.md) | Reject conflicting `Providers.Register` | US-001 | removed in 3.0 | removed in 3.0 | **Superseded** by [0013](adrs/0013-explicit-providers-only-remove-static-registry.md) |
| [0010](adrs/0010-warn-on-open-operator-znodes-when-acls-are-configured.md) | Warn on open operator znodes when ZK ACLs are configured | US-007 | `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs` | `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs` | **Accepted** (2026-10-18) |
| [0011](adrs/0011-zookeeper-acls-by-default-in-next-major-version.md) | ZooKeeper credentials required by default in 3.0 | US-007 | `src/Rebalancer.ZooKeeper/`, `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` (`DeprecationContractTests`, `ZooKeeperAclApiTests`), `tests/Rebalancer.IntegrationTests/ZooKeeper/` | **Accepted**; deprecation 2.2.0 (CR-0002), removal 3.0.0 (CR-0003) |
| [0012](adrs/0012-zookeeper-provider-entry-point-for-a-custom-service.md) | `ZooKeeperProvider` constructor for a caller-supplied `IZooKeeperService` | US-007 | `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs` | `tests/Rebalancer.UnitTests` (`DeprecationContractTests`) | **Accepted**; TM-0001 threat T4 |
| [0013](adrs/0013-explicit-providers-only-remove-static-registry.md) | Explicit providers only; static `Providers` removed in 3.0 | US-0015 (successor of US-001), US-012 | `src/Rebalancer.Core/RebalancerClient.cs`, `src/Rebalancer.Extensions.DependencyInjection/` | `tests/Rebalancer.UnitTests` (`DeprecationContractTests`) | **Accepted**; supersedes 0001, 0009 |
| [0014](adrs/0014-expire-stale-client-records.md) | Expire stale client records (SQL Server, Redis) | US-005, US-006 | `src/Rebalancer.SqlServer/Clients/`, `src/Rebalancer.Redis/Clients/`, `src/Rebalancer.Core/ClientOptions.cs` | `tests/Rebalancer.UnitTests` (`ClientRecordExpiryTests`), `tests/Rebalancer.IntegrationTests` (`SqlServerClientRecordTests`, `RedisClientRecordTests`) | **Accepted**; released in 3.0.0 |
| [0015](adrs/0015-store-redis-clients-per-resource-group.md) | One Redis client hash per resource group | US-006 | `src/Rebalancer.Redis/Clients/ClientService.cs`, `src/Rebalancer.Redis/Utils/RedisHash.cs` | `tests/Rebalancer.IntegrationTests/Redis/RedisClientRecordTests.cs` | **Accepted**; released in 3.0.0 |
| [0016](adrs/0016-rebalancer-client-owns-its-provider.md) | `RebalancerClient` owns and disposes its provider | US-003, US-006 | `src/Rebalancer.Core/RebalancerClient.cs` | `tests/Rebalancer.UnitTests` (`RebalancerClientLifecycleTests`, `DiRegistrationTests`) | **Accepted**; supersedes 0006 |
| [0017](adrs/0017-redis-lease-timing-and-atomic-updates.md) | Redis leases: server clock, compare-and-set | US-006 | `src/Rebalancer.Redis/Leases/` | `tests/Rebalancer.UnitTests` (`RedisLeaseDecisionTests`) | **Accepted**; supersedes 0006 |
| [0018](adrs/0018-opt-in-zookeeper-acls-in-2x.md) | Opt-in ZooKeeper ACLs in 2.x | US-007 | `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs`, `ZooKeeperProvider.cs` | `tests/Rebalancer.UnitTests` (`ZooKeeperAclApiTests`), `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs` | **Accepted**; supersedes 0006 |
| [0019](adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md) | Stop unresponsive followers holding reassigned resources | US-005, US-006, US-013 | `src/Rebalancer.SqlServer/Roles/Coordinator.cs`, `src/Rebalancer.Redis/Roles/Coordinator.cs` | `tests/Rebalancer.UnitTests` (`UnresponsiveFollowerTests`), `tests/Rebalancer.IntegrationTests` (`*PausedNodeTests`) | **Accepted**; released in 3.1.0 (CR-0004); option C declined for now (ADR-0021) |
| [0020](adrs/0020-per-group-redis-leases-and-resources.md) | Per-group Redis keys for leases and resource lists | US-0016 (3.x warning), US-0020 (4.0) | `src/Rebalancer.Redis/Utils/PerGroupKeyMigration.cs`, `Leases/`, `Clients/`, `Resources/` | `tests/Rebalancer.IntegrationTests/Redis/` (`RedisPerGroupKeysTests`, `RedisClientRecordTests`) | **Accepted**; 3.x warning in 3.2.0 (CR-0005, Implemented), layout in 4.0.0 (CR-0006, Implemented) |
| [0021](adrs/0021-assignment-generation-for-resource-handlers.md) | No fencing token for resource handlers for now | US-002 | none (option D) | `*PausedNodeTests` (window of ADR-0019) | **Accepted** (2026-10-19), option D; revisit on consumer demand |
| [0022](adrs/0022-rebalancing-telemetry-contract.md) | Telemetry contract: System.Diagnostics metrics and traces | US-0017 | `src/Rebalancer.Core/RebalancerTelemetry.cs`, `RebalancerClient.cs`, backends' coordinators and providers | `tests/Rebalancer.UnitTests` (`RebalancerClientTelemetryTests`), `tests/Rebalancer.IntegrationTests` (`*TelemetryTests`) | **Accepted**; 4.1.0 (CR-0007, Implemented) |
| [0023](adrs/0023-health-check-for-a-rebalancer-client.md) | Health check for a client in an optional package | US-0018, US-0021 | `src/Rebalancer.Extensions.Diagnostics.HealthChecks/` | `tests/Rebalancer.UnitTests` (`RebalancerClientHealthCheckTests`) | **Accepted**; 4.1.0 (CR-0007, Implemented) |
| [0025](adrs/0025-runnable-super-streams-example.md) | Runnable super-streams example (`RabbitMQ.Stream.Client`) | US-0023 | `examples/Rebalancer.RabbitMq.SuperStreamsExample` (planned) | not implemented | **Proposed**; supersedes 0024 |
| [0024](adrs/0024-dependencies-of-the-rabbitmq-examples.md) | Dependencies of the RabbitMQ examples: super-streams example, no `Consul` | US-0022, US-0023 | `examples/Rebalancer.RabbitMq.*`, `docs/guides/rabbitmq-ordered-consumer-groups.md` | manual run (example READMEs) | **Proposed** (revised 2026-10-20); implemented: super-streams example, guide, `Consul` removed |
| [0025](adrs/0025-pin-patched-system-security-cryptography-pkcs.md) | Pin patched System.Security.Cryptography.Pkcs (CVE-2026-84008) | US-005 | `src/Rebalancer.SqlServer/Rebalancer.SqlServer.csproj` | vulnerability scan; unit and SQL Server integration tests | **Proposed**; 4.2.1 (CR-0009, Submitted) |

### Release records

| Record | Status | Covers |
|--------|--------|--------|
| [CR-0001](change-records/CR-0001-coordination-safety-and-provider-ownership.md) | **Implemented** (2.1.0, `2952de6`); rollback dry-run not performed; [release notes](release-notes/2.1.0.md) | ADR 0006–0009, PRB fixes; Redis all-nodes-at-once upgrade |
| [CR-0002](change-records/CR-0002-release-2-2-0-deprecations.md) | **Implemented** (2.2.0, `ac6587a`); [release notes](release-notes/2.2.0.md) | Deprecations of ADR 0011, 0012, 0013 |
| [CR-0003](change-records/CR-0003-release-3-0-0.md) | **Implemented** (3.0.0, `f3efbcb`); [release notes](release-notes/3.0.0.md), [migration guide](release-notes/migrating-to-3.0.md) | ADR 0011–0015, US-0015 |
| [CR-0004](change-records/CR-0004-release-3-1-0.md) | **Implemented** (3.1.0, `dd094d4`); [release notes](release-notes/3.1.0.md) | ADR-0019, PRB-0003 |
| [CR-0005](change-records/CR-0005-release-3-2-0.md) | **Implemented** (3.2.0, `860b3d3`); [release notes](release-notes/3.2.0.md) | US-0016, ADR-0021, operator scripts, paused-node tests |
| [CR-0006](change-records/CR-0006-release-4-0-0.md) | **Implemented** (4.0.0, `b9b5f3f`); [release notes](release-notes/4.0.0.md), [migration guide](release-notes/migrating-to-4.0.md) | ADR-0020, US-0020 |
| [PRB-0001](problem-records/PRB-0001-demoted-coordinator-keeps-resources.md) | Closed (2.1.0) | US-005, US-006, US-013 |
| [PRB-0002](problem-records/PRB-0002-zookeeper-tests-ignored-their-provider.md) | Closed (2.1.0) | US-010 |
| [PRB-0003](problem-records/PRB-0003-resumed-follower-keeps-reassigned-resources.md) | Closed (3.1.0); follow-ups owned by dradoaica | US-005, US-006, US-013 |

### 4.1.0 records

| Record | Status | Covers |
|--------|--------|--------|
| [CR-0007](change-records/CR-0007-release-4-1-0.md) | **Implemented** (4.1.0, `a340daf`); [release notes](release-notes/4.1.0.md) | ADR-0022, ADR-0023, US-0017..0019 (US-0019: `*RandomisedTests`, governed by ADR-0002 / ADR-0019) |

### 4.2.0 records

| Record | Status | Covers |
|--------|--------|--------|
| [CR-0008](change-records/CR-0008-release-4-2-0.md) | **Implemented** (4.2.0, `7bd4ad8`); [release notes](release-notes/4.2.0.md) | US-0021 (Aborted state), health-check fix (ADR-0023 note), malformed Redis entries |

### 4.2.1 records

| Record | Status | Covers |
|--------|--------|--------|
| [CR-0009](change-records/CR-0009-release-4-2-1.md) | **Implemented** (4.2.1, `732ecd7`); [release notes](release-notes/4.2.1.md) | ADR-0025, PRB-0004 (CVE-2026-84008) |
| [PRB-0004](problem-records/PRB-0004-vulnerable-transitive-pkcs-in-sql-server-packages.md) | Root cause identified | `System.Security.Cryptography.Pkcs` in the SQL Server packages |

### Threat models

| Threat model | Status | Scope |
|--------------|--------|-------|
| [TM-0001](threat-models/TM-0001-zookeeper-coordination-and-acl.md) | **Accepted** revisions 1–3 | ZooKeeper coordination and ACLs |
| [TM-0002](threat-models/TM-0002-sql-server-coordination.md) | **Accepted** revisions 1–3 | SQL Server coordination (`RBR` schema) |
| [TM-0003](threat-models/TM-0003-redis-coordination.md) | **Accepted** revisions 1–5 | Redis coordination (`RBR` keys) |

### Stories without a single ADR (tooling / quality)

| Story | Title | Links | Locations |
|-------|-------|-------|-----------|
| [US-009](user-stories/US-009-rabbitmq-tools-cli-for-queue-setup.md) | RabbitMQ tools CLI | README | `examples/Rebalancer.RabbitMq.Tools/` |
| [US-0022](user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md) | Ordered consumer group example (option A) | ADR-0002, ADR-0019, ADR-0021, ADR-0024 | `examples/Rebalancer.RabbitMq.*` |
| [US-0023](user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md) | Rebalancer or super streams guide | ADR-0024 | `docs/guides/rabbitmq-ordered-consumer-groups.md` |

### v2 documentation slice

| Story | Title | Status | Outcome |
|-------|-------|--------|---------|
| [US-014](user-stories/US-014-traceability-matrix-and-product-baseline.md) | Matrix + product baseline | Done | [rebalancer-product-baseline.md](initial-product-specs/rebalancer-product-baseline.md), this matrix, [architecture overview](architecture/rebalancer-overview.md), README **Documentation** section |

---

**Maintenance:** At release boundaries, align story **Done** states and add a change record reference in `docs/change-records/` if your process requires it.
