# Rebalancer (forked from [Jack Vanlightly](https://github.com/Vanlightly) and improved)

Create Kafka style consumer groups in other technologies. Rebalancer was born of the need for consumer groups with
RabbitMQ. But Rebalancer is completely technology agnostic and will balance activity over any group of resources across
a group of participating nodes.

## Use cases

- Create Kafka-like "consumer groups" with messaging technologies like RabbitMQ, SQS, etc.

- Consume a group of resources such as file shares, FTPs, S3 buckets between the instances of a scaled out application.

- Single Active-Consumer / Active-Backup

- Create an application cluster that consumes a single resource in a highly available manner. The cluster leader (
  Coordinator) consumes the single resource and the slaves (Followers) remain idle in backup in case the leader dies.

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

When a node becomes active it notifies the other nodes. One node is the Coordinator (leader) and the rest are Followers.
The Coordinator has the job to assign resources to nodes. It monitors the coming and going of nodes, as well as changes
to the number of resources available to the resource group. When any change happens to the nodes or resources then the
Coordinator triggers a rebalancing.

Different rebalancing algorithms exist:

- Leader based resource barrier

- Leader based global barrier

The rebalancing algorithm depends on the backend. With an RDBMS we use the following:

With an RDBMS we use the global barrier algorithm where:

1. The Coordinator orders all Followers to stop activity.

2. Once activity has stopped the Coordinator distributes the resource identifiers equally between the Followers and
   itself.

3. The final step is that the Coordinator notifies each Follower of the resources it has been assigned and can start its
   activity (consuming, reading, writing etc).

Other backends are suited to either.

Leader election determines who the Coordinator is. If the Coordinator dies, then a Follower takes its place. Leader
election and meta-data storage is performed via a consensus service (ZooKeeper, Etcd, Consul, Redis) or an RDBMS with
serializable transaction support (SQL Server, Oracle, PostgreSQL). All communication between nodes is also performed via
this backing meta-data store.

## Languages and Backends

Rebalancer is a suite of code libraries. It must be implemented in your language in order to use it. Also, different
backends will be available.

## Documentation

- [Traceability matrix](docs/traceability-matrix.md) — ADRs, user stories, and paths under `src/`, `examples/`, and
  `tests/`
- [Product baseline (.NET)](docs/initial-product-specs/rebalancer-product-baseline.md) — scope, non-goals, v2 themes
- [Architecture overview](docs/architecture/rebalancer-overview.md) — component diagram
- [Architectural decision records](docs/adrs/) — `docs/adrs/`
- [User stories](docs/user-stories/) — `docs/user-stories/`

### Contributors and AI agents

- [`.agents/AGENTS.md`](.agents/AGENTS.md) — canonical instructions for all agents; root [`AGENTS.md`](AGENTS.md) and
  vendor adapters defer to it.
- [`docs/initial-product-specs/README.md`](docs/initial-product-specs/README.md) — start here for spec-first product
  work.
- [`docs/cogov-a-sdlc/README.md`](docs/governance/README.md) — overview of the co-governed SDLC layout (single vs
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
The parameterless `RebalancerClient` constructor still uses static
`Providers.Register` ([ADR 0005](docs/adrs/0005-dependency-injection-for-provider-registration-v2.md)).

### Building and testing

- Solution: [Rebalancer.slnx](Rebalancer.slnx)
- **`tests/Rebalancer.UnitTests`** — DI registration tests (no Docker).
- **`tests/Rebalancer.IntegrationTests`** — Redis/SQL Server tests use **Testcontainers** (**Docker**; tests **skip** if
  unavailable). ZooKeeper barrier tests live under `ZooKeeper/` and require a running **ZooKeeper** cluster (see [
  `tests/Rebalancer.IntegrationTests/ZooKeeper/Helpers/ZkHelper.cs`](tests/Rebalancer.IntegrationTests/ZooKeeper/Helpers/ZkHelper.cs)).

### Repository layout

| Folder                   | Contents                                                                                                    |
|--------------------------|-------------------------------------------------------------------------------------------------------------|
| [`src/`](src/)           | Library projects (`Rebalancer.Core`, backends, `Rebalancer.Extensions.DependencyInjection`).                |
| [`examples/`](examples/) | Sample host and CLI tools (`Rebalancer.RabbitMq.ExampleWithSqlServerBackend`, `Rebalancer.RabbitMq.Tools`). |
| [`tests/`](tests/)       | `Rebalancer.UnitTests`, `Rebalancer.IntegrationTests`.                                                      |
