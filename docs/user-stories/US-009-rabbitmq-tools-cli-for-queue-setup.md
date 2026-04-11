# User story: RabbitMQ tools CLI for queue and group setup

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-009                                             |
| **Status**      | Done                                               |
| **Priority**    | Could                                              |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As an** operator  
**I want** a command-line tool under `examples/Rebalancer.RabbitMq.Tools` to create or manage RabbitMQ queue inventory aligned with a consumer group and backend connection  
**So that** I can provision broker objects consistently before or alongside Rebalancer clients

## Context

`examples/Rebalancer.RabbitMq.Tools/Program.cs` parses command-line configuration (`Command`, `Backend`, `ConnString`, RabbitMQ host/credentials, consumer group, exchange, queue count/prefix, lease expiry). Commands such as `create` interact with RabbitMQ management or connections as implemented.

## Acceptance criteria

1. Tool documents mandatory arguments: `Command`, `Backend`, `ConnString`, `ConsumerGroup`, `ExchangeName`, `QueueCount`, `QueuePrefix`, `LeaseExpirySeconds` (see `Program.cs` in `Rebalancer.RabbitMq.Tools`).
2. Tool supports at least the `create` command path for provisioning per current implementation.
3. Story explicitly references this project as **supporting** tooling, not the core `IRebalancerProvider` contract.

## Non-goals

- Full replacement for RabbitMQ operator or Terraform modules.
- Guaranteeing compatibility with every RabbitMQ version.

## Dependencies

- Decision records: N/A (tooling only; optional link to `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` if backend argument maps to store setup)
- Other stories: `docs/user-stories/US-008-run-rabbitmq-example-with-sql-server-backend.md`

## Security & compliance

CLI accepts passwords on the command line in examples—discourage in production logs; prefer environment variables or secret injection wrappers where extended.

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `examples/Rebalancer.RabbitMq.Tools/Program.cs`
- `examples/Rebalancer.RabbitMq.Tools/RabbitConnection.cs` (and related types)
- `README.md`
