---
id: US-011
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Should"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0001", "US-001" ]
---

# User story: Pluggable Rebalancer logging

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

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met" and "Docs" ticks verified by an agent against the code and docs; "Tests / checks
agreed with a team" confirmed by dradoaica on 2026-10-18; change-record traceability ticked after the 2.1.0 release. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) — `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md` (2.1.0)

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Logging/IRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/NullRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/ConsoleRebalancerLogger.cs`
- `src/Rebalancer.Core/Logging/MicrosoftRebalancerLogger.cs`
