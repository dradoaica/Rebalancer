---
id: US-008
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Could"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0002", "ADR-0004", "US-002", "US-005" ]
---

# User story: Run RabbitMQ sample with SQL Server backend

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

- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
- `README.md`
- `docs/adrs/0004-client-options-recovery-and-assignment-delay.md`

## Note (2026-10-19)

Superseded in part by [US-0022](US-0022-ordered-consumer-group-example-with-quorum-queues.md): the example now uses
quorum queues with single active consumer and dead-lettering, manual acknowledgement and a turn guard; the tools gained
`publish` and `unblock` and declare the new queue arguments. (Also outdated above: the example passes the provider to
`RebalancerClient`; `Providers.Register` was removed in 3.0, ADR-0013.)

