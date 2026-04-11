# User story: Pluggable Rebalancer logging

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-011                                             |
| **Status**      | Done                                               |
| **Priority**    | Should                                             |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** operator  
**I want** to supply an `IRebalancerLogger` implementation (or rely on safe defaults)  
**So that** coordination events can be correlated with client id and severity without forcing one logging stack on all hosts

## Context

ADR 0001 documents logging in a subsection. Implementations include null, console, and Microsoft extensions loggers under `src/Rebalancer.Core/Logging/`.

## Acceptance criteria

1. `IRebalancerLogger` defines `Debug`, `Info`, `Warn`, `Error` overloads and `SetMinimumLevel` per `IRebalancerLogger.cs`.
2. Providers that accept optional loggers fall back to `NullRebalancerLogger` or equivalent when none is supplied (verify in `SqlServerProvider` / `RedisProvider` / `ZooKeeperProvider` constructors as applicable).
3. Story references ADR 0001 subsection on logging.

## Non-goals

- OpenTelemetry or structured log schema standardization.
- Centralized log shipping configuration.

## Dependencies

- Decision records: `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md` (logging subsection)
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

Log messages may include client identifiers; avoid logging connection strings or queue payloads unless redacted. Align with organizational logging policy.

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Logging/IRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/NullRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/ConsoleRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/MicrosoftRebalancerLogger.cs`
