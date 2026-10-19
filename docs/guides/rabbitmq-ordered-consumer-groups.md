# RabbitMQ: ordered consumer groups with Rebalancer or with super streams

When many consumers share a stream of messages that must stay in order per key (for example the turns of a chatbot
conversation), there are two good setups on RabbitMQ. This guide compares them and says when to use which
([US-0023](../user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md),
[ADR-0024](../adrs/0024-dependencies-of-the-rabbitmq-examples.md)).

- **Option A, Rebalancer over quorum queues:** a consistent-hash exchange routes each message by its key to one of N
  quorum queues with single active consumer and dead-lettering; Rebalancer spreads the queues over the nodes. Runnable
  in this repository: [the RabbitMQ example](../../examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/README.md).
- **Option B, super streams:** a partitioned stream routes each message by its key to one of N partitions; single
  active consumer gives each partition one consumer of a named group, and the broker spreads partitions over the
  group. No Rebalancer is involved. Runnable in this repository:
  [the super-streams example](../../examples/Rebalancer.RabbitMq.SuperStreamsExample/README.md).

## Background: what RabbitMQ provides natively

- **Single active consumer** (since RabbitMQ 3.8): the broker delivers a queue's messages to one consumer at a time and
  fails over to the next registered consumer when it goes away. Available on classic and quorum queues.
- **Super streams** and **single active consumer for streams** (since RabbitMQ 3.11): partitioned streams whose
  partitions each have one active consumer, preserving order within a partition.
- **Streams have no dead-lettering**: a stream is an append-only log and is read without removing messages.
- **Quorum queues count deliveries** and dead-letter a message after `x-delivery-limit` deliveries (default 20 since
  4.0). In RabbitMQ 4.x, `basic.reject` with requeue counts toward the limit but `basic.nack` with requeue does not.
  At-least-once dead-lettering needs `x-dead-letter-strategy: at-least-once` with `x-overflow: reject-publish`.

Rebalancer itself predates single active consumer: it was written when RabbitMQ had no way to keep one consumer per
queue with failover.

## The comparison

**Who keeps one consumer per partition.** In A, single active consumer on each queue makes the broker deliver to one
consumer; Rebalancer only decides which node subscribes to which queue, which is how the load is spread. Keep single
active consumer on: Rebalancer's lease alone lets two nodes overlap for a moment after a long pause (ADR-0019,
ADR-0021). In B, the broker does both: one active consumer per partition, and partitions spread over the group.

**Order when a node fails.** Both keep a key's order. In A, the active consumer's unacknowledged messages return to its
queue and the next consumer gets them first; in B, the next consumer continues from the stored offset. In both, a
frozen node blocks its partitions until the broker notices the dead connection (heartbeat timeout), but no two
consumers overlap.

**Scaling and deploys.** In A, every node that joins or leaves makes Rebalancer stop all consumers before reassigning
(the SQL Server and Redis backends wait in 5-second barrier steps), so every key pauses for a few seconds. In B, only
the partitions that change consumer pause.

**A message that keeps failing.** A gets retry counting and dead-lettering from quorum queues. B has neither; the
application retries and writes failed messages to its own parking stream or queue. For conversations, neither is
enough on its own: if a failed turn is dead-lettered and the next turn is answered, the conversation stops making
sense. In both options the application should block the conversation after a final failure and hold back its later
turns until the failure is handled. The example in this repository does exactly that.

**After processing.** In A, acknowledged messages are gone. In B, messages stay for the retention period, so a
conversation can be replayed for auditing, debugging or rebuilding state.

**What you run.** A needs RabbitMQ with the consistent-hash exchange plugin, and SQL Server, Redis or ZooKeeper for
Rebalancer; clients use AMQP 0-9-1 as usual. B needs the stream plugin and a stream protocol client (for .NET,
`RabbitMQ.Stream.Client`); the application tracks offsets.

**Without Rebalancer, A still works** (every node subscribes to every queue, the broker picks one active consumer per
queue) but the load can be very uneven: after a restart one node may be active on every queue. Rebalancer's value in
A is this balance, plus its health check and telemetry.

**Changing the number of partitions** moves some keys to other partitions in both. In A, messages already queued stay
where they are, so a key's order can break during the change; plan it as a migration with traffic drained.

## In both options

- Keep a turn number (or other sequence) per key in the application and reject stale or duplicate messages; delivery
  is at least once, and no option removes duplicates after a failover.
- Handle one message at a time per partition (prefetch 1, or a serial queue per key inside the consumer).
- Keep retries short: while a partition retries one message, its other keys wait.

## Which one to choose

- **Super streams (B)** when order must hold through deploys and scaling, and replay is useful. Expect to write retry
  and parking logic.
- **Quorum queues without Rebalancer (A without it)** when you need ordinary AMQP queues with dead-lettering and a
  delivery limit, and you run only a few consumer nodes; accept some imbalance.
- **Quorum queues with Rebalancer (A)** when that imbalance matters and you already run SQL Server or Redis. Keep
  single active consumer on, and accept a short pause of all keys on each scaling event.

## Sources

- RabbitMQ documentation: [Consumers, single active consumer](https://www.rabbitmq.com/docs/consumers),
  [Streams, super streams](https://www.rabbitmq.com/docs/streams),
  [Quorum queues, poison message handling and dead-lettering](https://www.rabbitmq.com/docs/quorum-queues),
  [RabbitMQ 3.8 release overview](https://www.rabbitmq.com/blog/2019/11/11/rabbitmq-3-8-release-overview) (checked
  2026-10-19).
- This repository: ADR-0019, ADR-0021, ADR-0024; both examples' verification (RabbitMQ 4.3.6) in their READMEs.
