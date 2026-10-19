---
id: US-0023
status: In progress
date: 2026-10-19
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Could"
ssdlc-phase: Coding
depends-on: [ "ADR-0024", "US-0022" ]
---

# User story: Show RabbitMQ super streams as the alternative for ordered consumer groups

## Story

**As a** developer choosing how to run an ordered consumer group on RabbitMQ (e.g. chatbot conversations)  
**I want** a clear comparison of Rebalancer over quorum queues (option A) and RabbitMQ super streams (option B)  
**So that** I pick the right one, and can build option B without Rebalancer when it fits better

## Context

Review of 2026-10-19: for strict per-conversation order through deploys and scaling, super streams with single active
consumer are often the better fit (broker-enforced, only affected partitions switch, replay), at the cost of writing
retry and parking logic (no dead-lettering). ADR-0024 (Proposed, revised 2026-10-20) adds a runnable example with `RabbitMQ.Stream.Client`
next to the guide.

## Acceptance criteria

If ADR-0024 is accepted with the guide (its option B):

1. `docs/guides/rabbitmq-ordered-consumer-groups.md` compares option A (US-0022) and super streams on: who enforces one
   consumer, order during failover, effect of scaling, failed turns (dead-lettering vs own parking logic), retention
   and replay, infrastructure, client model, partition-count changes; with the recommendation of the review.
2. It states that both options need an application-level turn number per conversation, and that a failed turn must
   block its conversation rather than be skipped.
3. RabbitMQ feature versions are cited with links to RabbitMQ's documentation (verified when written).
4. The README "Use cases" and the example README link the guide.

Added 2026-10-20 (ADR-0024 revised: the maintainer asked for a runnable example as well), in addition:

5. `examples/Rebalancer.RabbitMq.SuperStreamsExample` publishes M conversations × T turns to a super stream with N
   partitions, routed by conversation ID, and consumes it with single active consumer and server-side offset tracking.
6. Failed turns are retried, then written to a parking stream, and the conversation is blocked; a turn-number guard
   rejects stale and duplicate turns.
7. Verified manually with two consumers: kill one while publishing; every conversation stays in order.

## Non-goals

- Changing Rebalancer for streams (a stream partition is not a Rebalancer resource in either option).
- Automated tests against RabbitMQ.

## Dependencies

- Decision records: `docs/adrs/0024-dependencies-of-the-rabbitmq-examples.md` (Proposed: runnable example and guide)
- Other stories: `docs/user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md`

## Security & compliance

None for the guide. For the code example: the stream client's license (Apache-2.0 AND MPL-2.0, ADR-0024) must be
accepted.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural") – ADR-0024 (Proposed, revised 2026-10-20)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – criteria 5–7: `examples/Rebalancer.RabbitMq.SuperStreamsExample`, verified manually on
  2026-10-20 (RabbitMQ 4.3.6, SQL Server 2022; results in its README). Criteria 1–4, the guide: `docs/guides/rabbitmq-ordered-consumer-groups.md`, linked
  from the README "Use cases" and the example README; RabbitMQ facts checked against rabbitmq.com on 2026-10-19
- [ ] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/adrs/0024-dependencies-of-the-rabbitmq-examples.md`
- `docs/user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md`
