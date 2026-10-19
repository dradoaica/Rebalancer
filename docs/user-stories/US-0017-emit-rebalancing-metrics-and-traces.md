---
id: US-0017
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19)
priority: "Could"
ssdlc-phase: Coding
depends-on: [ "ADR-0022", "ADR-0002", "ADR-0003", "US-011", "TM-0001" ]
---

# User story: Emit rebalancing metrics and traces

## Story

**As an** operator running Rebalancer nodes  
**I want** standard metrics and traces for leadership, rebalances and assignments  
**So that** I can see in my existing OpenTelemetry tooling how often groups rebalance, how long they stay unassigned,
and which node coordinates

## Context

Product baseline roadmap theme "Next: OpenTelemetry" (`docs/initial-product-specs/rebalancer-product-baseline.md`).
Today the library only logs (US-011). `System.Diagnostics.Metrics` and `ActivitySource` come from the
`System.Diagnostics.DiagnosticSource` package, which `Rebalancer.Core` already resolves transitively (10.0.13, through
`Microsoft.Extensions.Logging.Abstractions`), so no package is new. The instrument names are a public contract:
ADR-0022 (Accepted) decides the API and the names.

## Acceptance criteria

1. A `Meter` and an `ActivitySource`, both named `Rebalancer`, exist in `Rebalancer.Core`, with the instruments, units
   and tags of ADR-0022; no OpenTelemetry package in any library (hosts add their own exporter).
2. Counters per backend and group: rebalances started and completed, coordinator elections, assignment and
   unassignment events; a histogram of the time from unassignment to the next assignment.
3. One activity per rebalance on the coordinator, with the group, backend and fencing token (or epoch) as tags.
4. No resource names, connection strings or credentials in tags.
5. Unit tests with a `MeterListener` and an `ActivityListener` cover criteria 2–4 for SQL Server and Redis.

## Non-goals

- Exporters, dashboards or alert rules.
- ZooKeeper beyond the same counters (its rebalancing modes may need separate measures).

## Dependencies

- Decision records: `docs/adrs/0022-rebalancing-telemetry-contract.md` (Accepted); `docs/adrs/0002-…md`,
  `docs/adrs/0003-…md` (protocol events).
- Other stories: `docs/user-stories/US-011-pluggable-rebalancer-logging.md`

## Security & compliance

Telemetry leaves the process; criterion 4 keeps resource names (which may be sensitive, TM-0001 I1) and secrets out.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural") – ADR-0022 (Accepted 2026-10-19)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – `src/Rebalancer.Core/RebalancerTelemetry.cs`, the backends' coordinators and providers;
  criterion 5 by `RebalancerClientTelemetryTests` (unit) and `SqlServerTelemetryTests` / `RedisTelemetryTests`
  (integration, Docker: elections, rebalances and activity are emitted by the backends, not by `RebalancerClient`)
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-19)
- [x] Docs / runbooks updated if needed – release notes 4.1.0, README
- [x] Traceable to change record (if released) - `docs/change-records/CR-0007-release-4-1-0.md`

## Sources

- `docs/adrs/0022-rebalancing-telemetry-contract.md`
- `docs/initial-product-specs/rebalancer-product-baseline.md` (Roadmap themes)
- `docs/user-stories/US-011-pluggable-rebalancer-logging.md`
- `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md`
