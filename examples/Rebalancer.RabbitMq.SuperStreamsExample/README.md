# Ordered consumer group with RabbitMQ super streams (option B, no Rebalancer)

The same chatbot-style scenario as the [option A example](../Rebalancer.RabbitMq.QuorumQueuesExample/README.md),
built on RabbitMQ super streams instead of Rebalancer
([US-0023](../../docs/user-stories/US-0023-super-streams-alternative-for-ordered-consumer-groups.md),
[ADR-0024](../../docs/adrs/0024-dependencies-of-the-rabbitmq-examples.md)). Which one to choose:
[the guide](../../docs/guides/rabbitmq-ordered-consumer-groups.md).

## How it fits together

- A **super stream** with N partitions (`chat-0` … `chat-3`); the producer routes each turn by its conversation ID, so
  a conversation's turns stay in one partition, in order.
- Consumers with the same **reference** (`EXAMPLE_REFERENCE`) form a group; with **single active consumer** the broker
  gives each partition to one of them and moves it when a consumer leaves. No Rebalancer and no coordination store.
- **Offsets are stored on the server** after each turn; a consumer that takes over a partition continues after the
  last stored offset.
- The same **turn guard** as option A (`ConversationGuard`, SQL Server) skips turns delivered again after a failover and
  never processes past a gap.
- Streams have **no dead-lettering**: a failed turn is retried in place, then written to the **parking stream**
  (`<super stream>-parking`) and its conversation is blocked; later turns of a blocked conversation are parked too.

## Run it

Prerequisites: Docker and the .NET 10 SDK; RabbitMQ 3.11 or later with the stream plugin. From the repository root:

```bash
docker run -d --name rabbit -p 5552:5552 -p 15672:15672 rabbitmq:4-management
docker exec rabbit rabbitmq-plugins enable rabbitmq_stream rabbitmq_stream_management
docker run -d --name sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=<strong password>' -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
# create a database, e.g. RabbitMqScaling (the conversation table is created by "create")

export EXAMPLE_SQL='Server=localhost;Database=RabbitMqScaling;User Id=sa;Password=<strong password>;TrustServerCertificate=true'
X="dotnet run --project examples/Rebalancer.RabbitMq.SuperStreamsExample --"

$X create                       # super stream, parking stream, conversation table (safe to run again)
EXAMPLE_NODE=A EXAMPLE_FAIL_CONVERSATION=conversation-7 EXAMPLE_FAIL_TURN=3 $X consume    # terminal 1
EXAMPLE_NODE=B $X consume                                                               # terminal 2
$X publish 20 10                # 20 conversations x 10 turns
```

Stop or kill a consumer while it works: the other takes over its partitions at once (the broker sees the closed
connection; a frozen process is noticed at the heartbeat timeout) and continues after the stored offsets.

| Variable                                         | Default             | Meaning                                                                                          |
|--------------------------------------------------|---------------------|--------------------------------------------------------------------------------------------------|
| `EXAMPLE_SQL`                                    | (required)          | SQL Server connection string (conversation table)                                                |
| `EXAMPLE_STREAM_HOST`, `EXAMPLE_STREAM_PORT`     | `localhost`, `5552` | Stream protocol endpoint (all connections go here, also when the broker advertises another host) |
| `EXAMPLE_RABBIT_USER`, `EXAMPLE_RABBIT_PASSWORD` | `guest`, `guest`    | Credentials                                                                                      |
| `EXAMPLE_SUPER_STREAM`, `EXAMPLE_PARTITIONS`     | `chat`, `4`         | Super stream name and partition count (`create`)                                                 |
| `EXAMPLE_REFERENCE`                              | `chat-consumers`    | Consumer group name                                                                              |
| `EXAMPLE_ATTEMPTS`                               | `3`                 | Attempts of a turn before it is parked                                                           |
| `EXAMPLE_NODE`, `EXAMPLE_WORK_MS`                | machine name, `50`  | Name in the log, simulated work per turn                                                         |
| `EXAMPLE_FAIL_CONVERSATION`, `EXAMPLE_FAIL_TURN` | none                | That turn always fails                                                                           |

A blocked conversation is released the same way as in option A: replay its parked turns in order (they are kept in
the parking stream), then `Rebalancer.RabbitMq.Tools --Command unblock --ConnString … --Conversation <id>`.

## What was verified (2026-10-20, RabbitMQ 4.3.6, SQL Server 2022)

Two consumers, 20 conversations × 10 turns, conversation 7's turn 3 failing:

- When the second consumer joined, the broker split the 4 partitions 2/2.
- One consumer killed (SIGKILL) while processing: the other took its partitions within 10 ms and resumed after the
  stored offsets; no turn out of order; 8 conversations moved between consumers; one turn the killed consumer had
  committed but not yet stored was delivered again and skipped as a duplicate.
- Conversation 7: turn 3 parked after 3 attempts, conversation blocked at turn 2, turns 4–10 parked (8 messages in
  the parking stream); the 19 other conversations complete.
- One consumer stopped with SIGTERM: it stopped cleanly and a third consumer took its partitions at once; publishing
  turns 1–5 again produced only duplicates, all skipped.
