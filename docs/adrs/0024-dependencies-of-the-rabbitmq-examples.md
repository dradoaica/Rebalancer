---
id: ADR-0024
status: Accepted
date: 2026-10-20
decides: "the third-party package dependencies of the RabbitMQ example projects under examples/"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "US-0022", "US-0023", "US-0008", "US-0009", "ADR-0025" ]
supersedes: [ ]
superseded-by: [ ]
---

# Dependencies of the RabbitMQ example projects: add a super-streams example, remove Consul

## Context and Problem Statement

The review of 2026-10-19 compared two ways to run an ordered consumer group on RabbitMQ: option A, Rebalancer over a
consistent-hash exchange with quorum queues, single active consumer and dead-lettering (US-0022, existing packages
only), and option B, RabbitMQ super streams with single active consumer and no Rebalancer (US-0023). B needs a client
for the RabbitMQ stream protocol, a package the repository does not use today. Adding or removing a dependency is
Tier 1 by default (`.agents/AGENTS.md`, triage rules), also in an example that is not packaged.

This record decides which packages the example projects may reference.

**Facts (verified 2026-10-19/20):**

* `RabbitMQ.Stream.Client` 1.12.2 (NuGet, latest stable) targets net8.0, net9.0 and net10.0 (the examples target
  net10.0). Its license is **Apache-2.0 AND MPL-2.0** (both apply), whereas `RabbitMQ.Client` 7.2.2, used today, is **Apache-2.0 OR MPL-2.0** (either may be chosen).
* Its documentation shows super streams (`StreamSystem.CreateSuperStream`, `PartitionsSuperStreamSpec`), a routed
  producer (`ProducerConfig.SuperStreamConfig.Routing`) and a consumer group with single active consumer (`ConsumerConfig.IsSuperStream`, `IsSingleActiveConsumer`, `Reference`, `ConsumerUpdateListener`, `StoreOffset`).
  Super streams need RabbitMQ 3.11 or later with the `rabbitmq_stream` plugin.
* `examples/Rebalancer.RabbitMq.Tools` referenced `Consul` 1.8.0, but no example source file used it (it only supplied
  `Newtonsoft.Json` transitively).

## Decision Drivers

* Readers can run and compare both options with the same scenario (US-0023).
* No unused or unreviewed dependencies, also in examples (they are copied into real projects).
* License obligations stay explicit.

## Considered Options

* **A. Runnable example:** a new project `examples/Rebalancer.RabbitMq.SuperStreamsExample` references
  `RabbitMQ.Stream.Client` 1.12.2 (new to the repository) and, for the same SQL Server turn guard as the option A
  example, `Microsoft.Data.SqlClient` 7.1.1 with the `System.Security.Cryptography.Pkcs` 10.0.13 pin of ADR-0025 (both already used by the other examples); keep the guide; remove `Consul`.
* **B. Guide only:** document super streams in a guide that links RabbitMQ's own examples; no new dependency; remove
  `Consul`.
* **C. Keep the dependencies as they are:** no super-streams material, `Consul` stays.

## Decision Outcome

Chosen option: **A** (accepted by dradoaica, 2026-10-20), as requested by the maintainer (this record first proposed B and was revised in
place before reaching the shared trunk).

The strongest argument against A is B: the super-streams example contains no Rebalancer code, so the repository
maintains, upgrades and license-reviews a client it does not otherwise use, to show an alternative to itself; and the
stream client's "AND" license adds MPL-2.0 obligations that `RabbitMQ.Client` lets you avoid. Given that, A is still
proposed because the maintainer wants both setups runnable side by side, and the cost is contained: one example
project, not a library package; nothing that ships to NuGet references the stream client. C keeps an unused
dependency.

Under every option, `Consul` is removed from `Rebalancer.RabbitMq.Tools` (its transitive `Newtonsoft.Json` use is
replaced by `System.Text.Json`).

### Consequences

* Good, because both options of the guide can be run and compared (conversations, turns, a failing turn, a node
  stopped or killed).
* Good, because an unused dependency leaves the repository.
* Bad, because the repository carries a stream client to keep current, under Apache-2.0 AND MPL-2.0.
* Neutral: only the example project references it; library packages and their dependency graphs are unchanged.

### Confirmation

* `dotnet list package` shows `RabbitMQ.Stream.Client` only in `examples/Rebalancer.RabbitMq.SuperStreamsExample`, no
  `Consul` anywhere, and no vulnerable package (`--vulnerable --include-transitive`).
* The super-streams example is verified manually like US-0022 (RabbitMQ 4.x with the stream plugin): order per
  conversation holds when a consumer is stopped or killed.

## Pros and Cons of the Options

### A. Runnable example

* Good, because both options can be run and compared.
* Bad, because of a new dependency with an "AND" license, in example code that does not use Rebalancer.

### B. Guide only

* Good, because there is no new dependency and little to maintain.
* Bad, because the comparison is prose, not code.

### C. No change

* Bad, because an unused dependency stays and readers get no guidance.

## More Information

* Stories: [US-0022](../user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md) (option A example,
  existing packages), [US-0023](../user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md)
  (guide and super-streams example).
* Existing examples: [US-0008](../user-stories/US-0008-run-rabbitmq-example-with-sql-server-backend.md),
  [US-0009](../user-stories/US-0009-rabbitmq-tools-cli-for-queue-setup.md).
* Client documentation: https://rabbitmq.github.io/rabbitmq-stream-dotnet-client/stable/htmlsingle/index.html
* Guide: [RabbitMQ: ordered consumer groups](../guides/rabbitmq-ordered-consumer-groups.md).
