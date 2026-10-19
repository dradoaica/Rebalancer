---
id: US-0022
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-20)
priority: "Should"
ssdlc-phase: Coding
depends-on: [ "ADR-0002", "ADR-0019", "ADR-0021", "US-008", "US-009" ]
---

# User story: RabbitMQ example of an ordered consumer group with quorum queues, single active consumer and dead-lettering

## Story

**As a** developer evaluating Rebalancer with RabbitMQ  
**I want** the RabbitMQ example to show a safe, ordered consumer group (e.g. chatbot conversations)  
**So that** I can copy a setup that keeps each conversation in order through failover, scaling and failed messages

## Context

Review of 2026-10-19 ("option A"): the example already combines a consistent-hash exchange with Rebalancer assigning
queues to nodes, but it uses classic queues, automatic acknowledgement, no single active consumer, no dead-lettering
and no ordering key. With Rebalancer's lease as the only guard, two nodes can consume one queue after a pause (ADR-0019; no fencing token, ADR-0021), and automatic acknowledgement loses messages on a crash. In option A the
broker enforces one consumer per queue (single active consumer) and Rebalancer only balances queues across nodes.

## Acceptance criteria

1. `Rebalancer.RabbitMq.Tools create` declares each queue as a quorum queue with `x-single-active-consumer: true`,
   `x-delivery-limit` (configurable, default 5), `x-dead-letter-exchange`, `x-dead-letter-strategy: at-least-once` and
   `x-overflow: reject-publish`; it declares the dead-letter exchange and queue; running it twice changes nothing.
2. `Rebalancer.RabbitMq.Tools publish` sends M conversations × T turns with the conversation ID as routing key and the
   turn number in the message, so per-conversation order can be checked.
3. The consumer acknowledges manually, sets prefetch 1 per queue and handles one message at a time per queue; a failed
   turn is retried in the consumer (up to the delivery limit, with a short back-off) and then rejected without requeue,
   so it is dead-lettered. (Corrected 2026-10-19 before approval: on RabbitMQ 4.x `basic.nack` with requeue does not
   count toward `x-delivery-limit` (observed with 4.3.6; RabbitMQ's quorum-queue documentation); retrying in the
   consumer keeps order and the limit stays the backstop for redeliveries after a consumer crash.)
4. The consumer keeps the last processed turn per conversation in SQL Server and updates it conditionally; a stale or
   duplicate turn is acknowledged and skipped. A conversation whose turn was dead-lettered is marked blocked; its later
   turns are rejected without requeue (parked in the dead-letter queue) until an operator unblocks it.
5. A fault-injection option makes one conversation's turn fail, to show retry, dead-lettering and blocking.
6. One connection per process with a channel per queue; no `Thread.Sleep` polling; stopping waits for consumers; the SQL
   Server and RabbitMQ connection settings come from arguments or environment (no Windows-only default).
7. An example README explains how to run it with Docker (RabbitMQ with the consistent-hash plugin, SQL Server), and
   why single active consumer must stay on (Rebalancer balances; the broker enforces one consumer).
8. Verified manually with two consumer processes: kill one while publishing, and every conversation's turns are
   processed in order (the log shows no out-of-order turn); the injected failure ends in the dead-letter queue.

Project renamed on 2026-10-20 from `Rebalancer.RabbitMq.ExampleWithSqlServerBackend` to
`Rebalancer.RabbitMq.QuorumQueuesExample`, matching `Rebalancer.RabbitMq.SuperStreamsExample` (US-0023).

## Non-goals

- Super streams (US-0023).
- Automated tests against RabbitMQ (would need `Testcontainers.RabbitMq`, a new dependency).
- Removing the unused `Consul` reference from the tools project (ADR-0024).

## Dependencies

- Decision records: `docs/adrs/0002-…md` (protocol), `docs/adrs/0019-…md`, `docs/adrs/0021-…md` (the overlap window
  the broker-side single active consumer closes for RabbitMQ)
- Other stories: `docs/user-stories/US-008-run-rabbitmq-example-with-sql-server-backend.md`,
  `docs/user-stories/US-009-rabbitmq-tools-cli-for-queue-setup.md`. US-008's context still says the example "wires
  `Providers.Register`" (removed in 3.0); a dated note there is part of this story.

## Security & compliance

Example only. Credentials come from arguments or environment, never from source; no production connection strings.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – `examples/Rebalancer.RabbitMq.Tools` (`create`, `publish`, `unblock`),
  `examples/Rebalancer.RabbitMq.QuorumQueuesExample` (`QueueConsumers`, `ConversationGuard`, `Settings`); criterion 8
  verified manually on 2026-10-19 (RabbitMQ 4.3.6, SQL Server 2022), results in the example README
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-20)
- [x] Docs / runbooks updated if needed – example README, US-008/US-009 notes
- [x] Traceable to change record (if released) - N/A: examples and docs only, not part of a package release

## Sources

- `examples/Rebalancer.RabbitMq.QuorumQueuesExample/Program.cs`, `examples/Rebalancer.RabbitMq.Tools/QueueManager.cs`
- `src/Rebalancer.Core/ClientOptions.cs` (`OnAssignmentDelay`: RabbitMQ redelivery order)
- `docs/adrs/0019-stop-unresponsive-followers-holding-reassigned-resources.md`, `docs/adrs/0021-assignment-generation-for-resource-handlers.md`
