---
id: ADR-0024
status: Accepted
date: 2026-10-19
decides: "the third-party package dependencies of the RabbitMQ example projects under examples/"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "US-0022", "US-0023", "US-008", "US-009" ]
supersedes: [ ]
superseded-by: ""
---

# Dependencies of the RabbitMQ example projects

## Context and Problem Statement

The review of 2026-10-19 compared two ways to run an ordered consumer group on RabbitMQ: option A, Rebalancer over a
consistent-hash exchange with quorum queues, single active consumer and dead-lettering (US-0022, existing packages
only), and option B, RabbitMQ super streams with single active consumer and no Rebalancer (US-0023). B needs a client
for the RabbitMQ stream protocol, a package the repository does not use today. Adding or removing a dependency is
Tier 1 by default (`.agents/AGENTS.md`, triage rules), also in an example that is not packaged.

This record decides which packages the example projects may reference.

**Facts (verified 2026-10-19, NuGet):**

* `RabbitMQ.Stream.Client` 1.12.2 is the latest stable release; it targets net8.0, net9.0 and net10.0 (the examples
  target net10.0). Its license is **Apache-2.0 AND MPL-2.0** (both apply), whereas `RabbitMQ.Client` 7.2.2, used today,
  is **Apache-2.0 OR MPL-2.0** (either may be chosen).
* `examples/Rebalancer.RabbitMq.Tools` references `Consul` 1.8.0, but no example source file uses it.
* Super-stream support and single active consumer in `RabbitMQ.Stream.Client`: unverified – please confirm against the
  client's documentation before implementation.

## Decision Drivers

* The repository's purpose is Rebalancer; examples should show how to use it, or when not to.
* No unused or unreviewed dependencies, also in examples (they are copied into real projects).
* License obligations stay understood.

## Considered Options

* **A. Add `RabbitMQ.Stream.Client` 1.12.2 to a new example project for super streams (US-0023 as code), and remove the
  unused `Consul` reference.**
* **B. Document super streams instead of coding them:** a "RabbitMQ: Rebalancer or super streams?" guide that compares
  A and B and links RabbitMQ's own super-stream documentation; no new dependency. Remove `Consul`.
* **C. Keep the dependencies as they are:** no super-streams material, `Consul` stays.

## Decision Outcome

Chosen option: **B** (accepted by dradoaica, 2026-10-19).

The strongest argument against B is that a guide cannot be run: a working super-streams example next to the option A
example would let readers compare both on their own machine, which is what US-0023 asks for, and a code example is
more convincing than prose. Given that, B is still proposed because the example would contain no Rebalancer code at
all, so the repository would maintain, upgrade and license-review a client it does not otherwise use, purely to
document an alternative to itself. RabbitMQ publishes and maintains super-stream examples; a guide that links them
and states the trade-offs (ordering, dead-lettering, scaling pauses, replay) gives readers the decision they need
without that cost. A remains the right choice if the maintainers want runnable side-by-side examples; then the
license difference above must be accepted explicitly. C keeps an unused dependency.

Under either A or B, `Consul` is removed from `Rebalancer.RabbitMq.Tools`.

### Consequences

* Good, because no new package enters the repository, and an unused one leaves.
* Good, because readers still get a clear answer to "Rebalancer or super streams?".
* Bad, because option B cannot be tried from this repository; readers follow RabbitMQ's own examples.
* Bad, because the guide must be kept current with RabbitMQ (feature versions are cited, not tested here).

### Confirmation

* `dotnet list package` for both example projects shows no `Consul` and (B) no stream client.
* The guide is linked from the README and the example README (US-0022).

## Pros and Cons of the Options

### A. Code example with the stream client

* Good, because both options can be run and compared.
* Bad, because of a new dependency with an "AND" license, and example code that does not use Rebalancer.

### B. Guide only

* Good, because there is no new dependency and little to maintain.
* Bad, because the comparison is prose, not code.

### C. No change

* Bad, because an unused dependency stays and readers get no guidance.

## More Information

* Stories: [US-0022](../user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md) (option A example,
  existing packages), [US-0023](../user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md)
  (option B, shape depends on this record).
* Existing examples: [US-008](../user-stories/US-008-run-rabbitmq-example-with-sql-server-backend.md),
  [US-009](../user-stories/US-009-rabbitmq-tools-cli-for-queue-setup.md).
* RabbitMQ feature versions (single active consumer 3.8, super streams 3.11) are from memory: unverified – please
  confirm when writing the guide.
