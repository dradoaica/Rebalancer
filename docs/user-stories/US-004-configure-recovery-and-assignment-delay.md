# User story: Configure recovery and assignment delay

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-004                                             |
| **Status**      | Done                                               |
| **Priority**    | Should                                             |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** messaging integrator  
**I want** to enable automatic recovery with a restart delay and optionally delay `OnAssignment` after rebalancing  
**So that** transient failures and broker keep-alive behavior do not cause duplicate or out-of-order processing

## Context

`ClientOptions` defines `AutoRecoveryOnError`, `RestartDelay`, and `OnAssignmentDelay` with XML documentation referencing RabbitMQ-style keep-alive timing. Providers receive these options in `StartAsync`.

## Acceptance criteria

1. `RebalancerClient.StartAsync` passes the provided `ClientOptions` instance through to `IRebalancerProvider.StartAsync` unchanged (reference equality not required; values must flow).
2. XML documentation on `OnAssignmentDelay` remains the authoritative explanation of the RabbitMQ ordering scenario (see `ClientOptions.cs`).
3. Example app demonstrates non-default `AutoRecoveryOnError` and `RestartDelay` (`examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`).

## Non-goals

- Exactly-once delivery guarantees across all backends.
- Automatic calculation of delay from broker heartbeat settings.

## Dependencies

- Decision records: `docs/adrs/0004-client-options-recovery-and-assignment-delay.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

N/A; tuning parameters only. Ensure logs do not expose internal broker topology if considered sensitive.

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

- `docs/adrs/0004-client-options-recovery-and-assignment-delay.md`
- `src/Rebalancer.Core/ClientOptions.cs`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
