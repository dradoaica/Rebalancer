---
id: ADR-0025
status: Proposed
date: 2026-10-20
decides: "the third-party package dependencies of the RabbitMQ example projects under examples/"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0024", "US-0022", "US-0023" ]
supersedes: [ "ADR-0024" ]
superseded-by: ""
---

# Add a runnable super-streams example with RabbitMQ.Stream.Client

## Context and Problem Statement

ADR-0024 (Accepted 2026-10-19) chose a guide instead of a super-streams code example, so that the repository would
not take a stream client it does not otherwise use. On 2026-10-20 the maintainer asked for a runnable example as well,
`examples/Rebalancer.RabbitMq.SuperStreamsExample`, next to the option A example (US-0022), so both setups can be
tried and compared. That reverses ADR-0024's outcome for the same concern, so this record supersedes it.

**Facts (verified 2026-10-20):**

* `RabbitMQ.Stream.Client` 1.12.2 (NuGet, latest stable) targets net8.0, net9.0 and net10.0; license
  **Apache-2.0 AND MPL-2.0** (both apply), unlike `RabbitMQ.Client` (Apache-2.0 OR MPL-2.0).
* Its documentation shows super streams (`StreamSystem.CreateSuperStream`, `PartitionsSuperStreamSpec`), a routed
  producer (`ProducerConfig.SuperStreamConfig.Routing`) and a consumer group with single active consumer
  (`ConsumerConfig.IsSuperStream`, `IsSingleActiveConsumer`, `Reference`, `ConsumerUpdateListener`, `StoreOffset`).
  Super streams need RabbitMQ 3.11 or later with the `rabbitmq_stream` plugin.

## Decision Drivers

* Readers can run and compare both options (US-0023).
* License obligations are explicit and accepted.
* No unused dependency (unchanged from ADR-0024).

## Considered Options

* **A. Runnable example with `RabbitMQ.Stream.Client` 1.12.2**, in a new project
  `examples/Rebalancer.RabbitMq.SuperStreamsExample` only; keep the guide; remove `Consul` (done under ADR-0024).
* **B. Keep ADR-0024 (guide only).**

## Decision Outcome

Proposed option: **A**, as requested by the maintainer.

The strongest argument against A is ADR-0024's own: the example contains no Rebalancer code, so the repository
maintains, upgrades and license-reviews a client it does not otherwise use, to show an alternative to itself; and the
stream client's "AND" license adds MPL-2.0 obligations that `RabbitMQ.Client` lets you avoid. Given that, A is still
proposed because the maintainer wants both setups runnable side by side, and the cost is contained: one example
project, not a library package; nothing that ships to NuGet references the stream client.

### Consequences

* Good, because both options of the guide can be run and compared with the same scenario (conversations, turns, a
  failing turn, a node stopped or killed).
* Bad, because the repository now carries a stream client to keep current, under Apache-2.0 AND MPL-2.0.
* Neutral: only the example project references it; library packages and their dependency graphs are unchanged.

### Confirmation

* `dotnet list package` shows `RabbitMQ.Stream.Client` only in `examples/Rebalancer.RabbitMq.SuperStreamsExample`, and no
  `Consul` anywhere.
* The example is verified manually like US-0022 (RabbitMQ 4.x with the stream plugin): order per conversation holds
  when a consumer is stopped or killed.

## More Information

* Supersedes [ADR-0024](0024-dependencies-of-the-rabbitmq-examples.md) for the same concern; its `Consul` removal is
  already implemented and stays.
* Stories: [US-0023](../user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md) (gains the
  code-example criteria), [US-0022](../user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md).
* Client documentation: https://rabbitmq.github.io/rabbitmq-stream-dotnet-client/stable/htmlsingle/index.html
