# Ordered consumer group on RabbitMQ with Rebalancer (option A)

A chatbot-style example: many conversations, each a sequence of turns that must be handled in order, spread over
several consumer nodes ([US-0022](../../docs/user-stories/US-0022-ordered-consumer-group-example-with-quorum-queues.md)).
For when to choose this setup or RabbitMQ super streams instead, see the
[guide](../../docs/guides/rabbitmq-ordered-consumer-groups.md).

## How it fits together

- A **consistent-hash exchange** routes each message by its conversation ID to one of N **quorum queues**, so all turns
  of a conversation land in the same queue, in order.
- Each queue has **single active consumer** on: the broker delivers to one consumer at a time. Keep it on; Rebalancer
  alone cannot guarantee that, because after a long pause two nodes can briefly think they own a queue (ADR-0019).
- **Rebalancer** (SQL Server backend) spreads the queues evenly over the running nodes and moves them when a node
  joins, leaves or dies.
- The consumer acknowledges manually, takes one message at a time per queue (prefetch 1), and keeps each
  conversation's last processed turn in SQL Server (`Example.ConversationTurns`). A redelivered turn is skipped as a
  duplicate; a turn after a gap is never processed.
- A failed turn is retried a few times in the consumer, then **dead-lettered** to `<exchange>.dlq`, and its
  conversation is **blocked**: its later turns are parked in the same dead-letter queue instead of being answered out
  of context. The queues' `x-delivery-limit` remains a backstop for a turn that crashes the process.

## Run it

Prerequisites: Docker and the .NET 10 SDK. From the repository root:

```bash
docker run -d --name rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management
docker exec rabbit rabbitmq-plugins enable rabbitmq_consistent_hash_exchange
docker run -d --name sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=<strong password>' -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
# create a database (e.g. RabbitMqScaling) and run db/RebalanserSchema.sql and db/RebalanserTables.sql in it

export EXAMPLE_SQL='Server=localhost;Database=RabbitMqScaling;User Id=sa;Password=<strong password>;TrustServerCertificate=true'
T="dotnet run --project examples/Rebalancer.RabbitMq.Tools --"

# queues, dead-lettering, resource group and conversation table (safe to run again)
$T --Command create --Backend mssql --ConnString "$EXAMPLE_SQL" --ConsumerGroup chat --ExchangeName chat.hash \
   --QueueCount 4 --QueuePrefix chat --LeaseExpirySeconds 10 --HeartbeatSeconds 2 --DeliveryLimit 3

# two consumer nodes, in two terminals (EXAMPLE_FAIL_* makes one turn fail, to show dead-lettering)
EXAMPLE_NODE=A EXAMPLE_GROUP=chat EXAMPLE_DELIVERY_LIMIT=3 EXAMPLE_FAIL_CONVERSATION=conversation-7 EXAMPLE_FAIL_TURN=3 \
  dotnet run --project examples/Rebalancer.RabbitMq.QuorumQueuesExample
EXAMPLE_NODE=B EXAMPLE_GROUP=chat EXAMPLE_DELIVERY_LIMIT=3 \
  dotnet run --project examples/Rebalancer.RabbitMq.QuorumQueuesExample

# 20 conversations x 10 turns, routed by conversation ID
$T --Command publish --ExchangeName chat.hash --Conversations 20 --Turns 10
```

Then stop one node (Ctrl+C, or kill it) while it is working: the other takes over its queues and every conversation
continues in order. Ctrl+C and SIGTERM stop a node cleanly; after a kill, the others wait for the lease to expire
(`LeaseExpirySeconds`).

| Variable | Default | Meaning |
|---|---|---|
| `EXAMPLE_SQL` | (required) | SQL Server connection string (Rebalancer and the conversation table) |
| `EXAMPLE_RABBIT_HOST`, `_PORT`, `_USER`, `_PASSWORD` | `localhost`, `5672`, `guest`, `guest` | RabbitMQ connection |
| `EXAMPLE_GROUP` | `NotificationsGroup` | Rebalancer resource group (`--ConsumerGroup` of `create`) |
| `EXAMPLE_NODE` | machine name | Name in the log |
| `EXAMPLE_DELIVERY_LIMIT` | `5` | Attempts of a turn before it is dead-lettered (use `--DeliveryLimit`) |
| `EXAMPLE_WORK_MS` | `50` | Simulated work per turn |
| `EXAMPLE_FAIL_CONVERSATION`, `EXAMPLE_FAIL_TURN` | none | That turn always fails |

## A blocked conversation

After the failure is fixed, move the conversation's messages from `<exchange>.dlq` back to the exchange **in turn
order** (for example with the management UI or a shovel), then unblock it:

```bash
$T --Command unblock --ConnString "$EXAMPLE_SQL" --Conversation conversation-7
```

Parked turns of other conversations in the dead-letter queue are not affected.

## What was verified (2026-10-19, RabbitMQ 4.3.6, SQL Server 2022)

Two nodes, 20 conversations × 10 turns, conversation 7's turn 3 failing:

- One node killed (SIGKILL) while processing: 192 turns processed, 6 conversations moved to the other node, no turn
  out of order, no duplicate.
- Conversation 7: turn 3 dead-lettered after 3 attempts, conversation blocked at turn 2, turns 4–10 parked (8 messages
  in the dead-letter queue).
- One node stopped with SIGTERM: it finished its current turn and left; the other took all queues 6 seconds later; all
  200 turns processed in order; 3 redeliveries skipped as duplicates by the turn guard.
- On RabbitMQ 4.x, `basic.nack` with requeue does not count toward `x-delivery-limit` (an endless retry loop with the
  first version of this example), which is why the consumer retries in place.
