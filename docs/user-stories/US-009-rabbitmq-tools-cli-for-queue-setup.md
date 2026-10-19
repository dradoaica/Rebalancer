---
id: US-009
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Could"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0002", "US-008" ]
---

# User story: RabbitMQ tools CLI for queue and group setup

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

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met" and "Docs" ticks verified by an agent against the code and docs; "Tests / checks
agreed with a team" confirmed by dradoaica on 2026-10-18; change-record traceability is ticked once the release ships. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `examples/Rebalancer.RabbitMq.Tools/Program.cs`
- `examples/Rebalancer.RabbitMq.Tools/RabbitConnection.cs` (and related types)
- `README.md`
