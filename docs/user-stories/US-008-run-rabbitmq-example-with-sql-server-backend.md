# User story: Run RabbitMQ sample with SQL Server backend

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-008                                             |
| **Status**      | Done                                               |
| **Priority**    | Could                                              |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** developer evaluating Rebalancer  
**I want** a working sample that registers `SqlServerProvider` and consumes RabbitMQ queues for assigned resources  
**So that** I can see end-to-end how consumer-group style assignment maps to real brokers

## Context

`examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend` wires `Providers.Register` to `SqlServerProvider`, handles `OnAssignment` / `OnUnassignment` / `OnAborted`, and starts async consumers per assigned queue name using RabbitMQ.Client.

## Acceptance criteria

1. Sample registers `SqlServerProvider` with a configurable SQL connection string (hardcoded in sample as `(local)` example—documented as developer-local only).
2. Sample passes `ClientOptions` with `AutoRecoveryOnError` and `RestartDelay` to illustrate US-004 patterns.
3. On assignment, sample starts consumption for each resource string returned by `GetAssignedResources()`; on unassignment, sample cancels consumer tasks.

## Non-goals

- Production-grade RabbitMQ topology (HA queues, federation) beyond illustration.
- Automated CI for the sample against live RabbitMQ/SQL (unless already present).

## Dependencies

- Decision records: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`, `docs/adrs/0004-client-options-recovery-and-assignment-delay.md`
- Other stories: `docs/user-stories/US-005-coordinate-via-sql-server-backend.md`, `docs/user-stories/US-002-react-to-assignment-unassignment-and-abort.md`

## Security & compliance

Sample uses default RabbitMQ guest credentials and local SQL—suitable for dev only. Production deployments must use secrets management and least privilege.

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
- `README.md`
- `docs/adrs/0004-client-options-recovery-and-assignment-delay.md`
