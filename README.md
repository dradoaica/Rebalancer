# Rebalancer (forked from [Jack Vanlightly](https://github.com/Vanlightly) and improved)

Create Kafka style consumer groups in other technologies. Rebalancer was born of the need for consumer groups with
RabbitMQ. But Rebalancer is completely technology-agnostic and will balance activity over any group of resources across
a group of participating nodes.

## Use cases

- Create Kafka-like "consumer groups" with messaging technologies like RabbitMQ, SQS, etc. On RabbitMQ 3.8+ read
  [RabbitMQ: ordered consumer groups with Rebalancer or with super streams](docs/guides/rabbitmq-ordered-consumer-groups.md)
  first: single active consumer and super streams now cover much of this natively.

- Consume a group of resources such as file shares, FTPs, S3 buckets between the instances of a scaled-out application.

- Single Active-Consumer / Active-Backup

- Create an application cluster that consumes a single resource in a highly available manner. The cluster leader
  (Coordinator) consumes the single resource, and the slaves (Followers) remain idle in backup in case the leader dies.

### Balancing assignment of multiple resources over multiples nodes

![](https://raw.githubusercontent.com/dradoaica/Rebalancer/main/wiki/images/RebalancerMultipleNodesMultipleResources.png)

### Consuming from a single resource with backup nodes in case of failure

![](https://raw.githubusercontent.com/dradoaica/Rebalancer/main/wiki/images/RebalancerBackupNodes.png)

## Concepts

Rebalancer consists of (or will consist of when complete):

- a set of protocols for different resource allocation algorithms

- a set of TLA+ specifications that verify the protocols

- a set of code libraries for multiple languages and backends

The important terms are:

- Resource Group = Group of Nodes + Group of Resources

When a node becomes active, it notifies the other nodes. One node is the Coordinator (leader) and the rest are Followers
. The Coordinator has the job to assign resources to nodes. It monitors the coming and going of nodes, as well as
changes
to the number of resources available to the resource group. When any change happens to the nodes or resources, then the
Coordinator triggers a rebalancing.

Different rebalancing algorithms exist:

- Leader-based resource barrier
- Leader-based global barrier

The rebalancing algorithm depends on the backend. With an RDBMS we use the following:

With an RDBMS we use the global barrier algorithm where:

1. The Coordinator orders all Followers to stop activity.

2. Once activity has stopped, the Coordinator distributes the resource identifiers equally between the Followers and
   itself.

3. The final step is that the Coordinator notifies each Follower of the resources it has been assigned and can start its
   activity (consuming, reading, writing, etc.).

Other backends are suited to either.

Leader election determines who the Coordinator is. If the Coordinator dies, then a Follower takes its place. Leader
election and metadata storage are performed via a backing metadata store; this library implements ZooKeeper, Redis, and
SQL Server (serializable transactions). Other consensus services (etcd, Consul) or RDBMSs (Oracle, PostgreSQL) would
fit the same pattern but are not implemented. All communication between nodes is also performed via this backing
metadata store.

## Languages and Backends

This repository is the .NET implementation: `Rebalancer.Core` plus one package per backend (`Rebalancer.ZooKeeper`,
`Rebalancer.Redis`, `Rebalancer.SqlServer`) and optional packages for dependency injection
(`Rebalancer.Extensions.DependencyInjection`) and health checks (`Rebalancer.Extensions.Diagnostics.HealthChecks`).
Other languages would need their own implementation of the protocol.

## Documentation

- [RabbitMQ: ordered consumer groups with Rebalancer or with super streams](docs/guides/rabbitmq-ordered-consumer-groups.md)
  — guide and the two RabbitMQ examples
- [Traceability matrix](docs/traceability-matrix.md) — ADRs, user stories, and paths under `src/`, `examples/`, and
  `tests/`
- [Product baseline (.NET)](docs/initial-product-specs/rebalancer-product-baseline.md) — scope, non-goals, v2 themes
- [Architecture overview](docs/architecture/rebalancer-overview.md) — component diagram
- [Architectural decision records](docs/adrs/) — `docs/adrs/`
- [User stories](docs/user-stories/) — `docs/user-stories/`
- [Release notes 4.2.1](docs/release-notes/4.2.1.md) — **security:** CVE-2026-84008 in a SQL Server dependency; upgrade
- [Release notes 4.2.0](docs/release-notes/4.2.0.md) — SQL Server / Redis report `Aborted`; health-check and Redis fixes
- [Release notes 4.1.0](docs/release-notes/4.1.0.md) — metrics and traces, health check package, randomised tests
- [Release notes 4.0.0](docs/release-notes/4.0.0.md) and [migration guide](docs/release-notes/migrating-to-4.0.md) —
  Redis: each resource group in its own keys (breaking for Redis provisioning). **Known issue:** upgrade to 4.1.0 or
  later before rolling a Redis group back to 3.x
  ([details](docs/release-notes/4.0.0.md#known-issue-resources-doubled-after-a-rollback-fixed-in-410))
- [Release notes 3.2.0](docs/release-notes/3.2.0.md) — Redis warning ahead of 4.0, SQL Server least-privilege role,
  ZooKeeper znode runbook
- [Release notes 3.1.0](docs/release-notes/3.1.0.md) — unresponsive followers are told to stop (PRB-0003)
- [Release notes 3.0.0](docs/release-notes/3.0.0.md) and [migration guide](docs/release-notes/migrating-to-3.0.md) —
  breaking changes
- [Release notes 2.2.0](docs/release-notes/2.2.0.md) — deprecations ahead of 3.0
- [Release notes 2.1.0](docs/release-notes/2.1.0.md) — behavior changes, upgrade notes (Redis: upgrade all nodes at
  once)
- [Threat models](docs/threat-models/) — ZooKeeper, SQL Server, and Redis backends
- [Change records](docs/change-records/) and [problem records](docs/problem-records/) — releases and defects (renumbered
  on 2026-10-19; the READMEs map the old identifiers)

### Contributors and AI agents

- [`.agents/AGENTS.md`](.agents/AGENTS.md) — canonical instructions for all agents; root [`AGENTS.md`](AGENTS.md) and
  vendor adapters defer to it.
- [`docs/initial-product-specs/README.md`](docs/initial-product-specs/README.md) — start here for spec-first product
  work.
- [`README(cogov-a-sdlc).md`](README%28cogov-a-sdlc%29.md) — overview of the co-governed SDLC layout (single vs.
  multi-repo, `docs/` artifacts).

### Dependency injection (v2)

Reference `Rebalancer.Extensions.DependencyInjection` and register the backend you use, for example:

```csharp
var services = new ServiceCollection();
services.AddRebalancerSqlServer(connectionString);
await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<RebalancerClient>();
```

Alternatively use `AddRebalancerRedis`, `AddRebalancerZooKeeper`, or `AddRebalancerProvider(_ => new MyProvider(...))`.
Without DI, pass the provider explicitly: `new RebalancerClient(new SqlServerProvider(connectionString))`.

Since 3.0 there is no static `Providers.Register` registry and no parameterless `RebalancerClient()`
([ADR 0013](docs/adrs/0013-explicit-providers-only-remove-static-registry.md)). Upgrading from 2.x:
[migration guide](docs/release-notes/migrating-to-3.0.md).

### Provider lifetime

A `RebalancerClient` owns its provider: disposing the client (also after `StopAsync`) disposes a provider that
implements `IDisposable`. `RedisProvider` does, and closes its Redis connection. Use one provider instance per client;
the DI registrations above already resolve a new provider for every client
([ADR-0016](docs/adrs/0016-rebalancer-client-owns-its-provider.md)).

### Metrics, traces and health

Rebalancer emits metrics and traces through `System.Diagnostics`
([ADR-0022](docs/adrs/0022-rebalancing-telemetry-contract.md)); collect them with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(RebalancerTelemetry.MeterName))
    .WithTracing(t => t.AddSource(RebalancerTelemetry.ActivitySourceName));
```

The package `Rebalancer.Extensions.Diagnostics.HealthChecks` adds a health check for a client your host started
([ADR-0023](docs/adrs/0023-health-check-for-a-rebalancer-client.md)):
`services.AddHealthChecks().AddRebalancerClient("rebalancer", client)`. Instrument names and health states:
[release notes 4.1.0](docs/release-notes/4.1.0.md).

### Errors and `OnAborted`

`OnAborted` reports an error; it does not always mean the client has stopped. ZooKeeper raises it only when the client
gives up. SQL Server and Redis raise it for every error: with `ClientOptions.AutoRecoveryOnError` the client restarts
after `RestartDelay` and is assigned again; without it the client gives up and (since 4.2.0) reports
`ClientState.Aborted`. Check `GetCurrentState()` to know whether a client still takes part; the health check does
this for you.

### Operating the backends

- **SQL Server:** create the schema from [`db/`](db/) and give the nodes' login only the role `rebalancer_node`
  ([`db/RebalanserLeastPrivilege.sql`](db/RebalanserLeastPrivilege.sql)).
- **Redis (4.0):** a group lives in `RBR:Lease:{group}`, `RBR:Resources:{group}` and `RBR:Clients:{group}`; the first
  4.0
  node moves a 3.x group automatically. Provisioning, the per-group ACL user, and rollback:
  [migrating-to-4.0.md](docs/release-notes/migrating-to-4.0.md). Upgrade all nodes of a group at once.
- **ZooKeeper:** pass credentials (or an explicit ACL); secure the znodes you create with the runbook in
  [`src/Rebalancer.ZooKeeper/README.md`](src/Rebalancer.ZooKeeper/README.md).
- **All backends:** keep the lease expiry well above the longest pause a node can have (GC, VM freeze); a resumed node
  stops at its first tick, but its handlers may run briefly before that
  ([ADR-0019](docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md),
  [ADR-0021](docs/adrs/0021-assignment-generation-for-resource-handlers.md)).

### Building and testing

- Solution: [Rebalancer.slnx](Rebalancer.slnx)
- **`tests/Rebalancer.UnitTests`** — DI registration, provider lifecycle, API contract, lease decisions, record expiry,
  coordinator demotion, unresponsive followers (no Docker).
- **`tests/Rebalancer.IntegrationTests`** — Redis/SQL Server tests use **Testcontainers** (**Docker**; tests **skip** if
  unavailable): assignment safety, paused follower and stalled coordinator (`*PausedNodeTests`), client-record expiry,
  the SQL Server least-privilege role, the Redis per-group key move and ACL user, telemetry, and randomised runs
  (`*RandomisedTests`, 3 minutes each; replay a run with `REBALANCER_RANDOMISED_SEED`, cap it with
  `REBALANCER_RANDOMISED_TEST_MAX_MINUTES`). ZooKeeper barrier tests live under `ZooKeeper/` and require a running
  **ZooKeeper** cluster (see [
  `tests/Rebalancer.IntegrationTests/ZooKeeper/Helpers/ZkHelper.cs`](tests/Rebalancer.IntegrationTests/ZooKeeper/Helpers/ZkHelper.cs)).

A local 3-node ZooKeeper cluster on `localhost:2181-2183`, as the ZooKeeper tests expect (remove it with
`docker rm -f rbr-it-zk1 rbr-it-zk2 rbr-it-zk3 && docker network rm rbr-it-zk`):

```bash
docker network create rbr-it-zk
SERVERS="server.1=rbr-it-zk1:2888:3888;2181 server.2=rbr-it-zk2:2888:3888;2181 server.3=rbr-it-zk3:2888:3888;2181"
for i in 1 2 3; do
  docker run -d --name rbr-it-zk$i --hostname rbr-it-zk$i --network rbr-it-zk -p 218$i:2181 \
    -e ZOO_MY_ID=$i -e "ZOO_SERVERS=$SERVERS" zookeeper:3.8
done
```

The randomised ZooKeeper tests run 2–8 hours each (about 38 hours in total). Set
`REBALANCER_RANDOMISED_TEST_MAX_MINUTES` (e.g. `2`) to cap each test and run the suite as a smoke test.

### Repository layout

| Folder                   | Contents                                                                                                                                                                                               |
|--------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [`src/`](src/)           | Library projects (`Rebalancer.Core`, backends, `Rebalancer.Extensions.DependencyInjection`, `Rebalancer.Extensions.Diagnostics.HealthChecks`).                                                         |
| [`examples/`](examples/) | RabbitMQ examples: ordered consumer group with Rebalancer (`Rebalancer.RabbitMq.QuorumQueuesExample`, `Rebalancer.RabbitMq.Tools`) and with super streams (`Rebalancer.RabbitMq.SuperStreamsExample`). |
| [`tests/`](tests/)       | `Rebalancer.UnitTests`, `Rebalancer.IntegrationTests`.                                                                                                                                                 |
