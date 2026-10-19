---
id: US-0017
status: Draft
date: 2026-10-19
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Could"
ssdlc-phase: Planning
depends-on: [ "ADR-0001", "ADR-0002", "ADR-0003", "US-011" ]
---

# User story: Emit rebalancing metrics and traces

## Story

**As an** operator running Rebalancer nodes  
**I want** standard metrics and traces for leadership, rebalances and assignments  
**So that** I can see in my existing OpenTelemetry tooling how often groups rebalance, how long they stay unassigned,
and which node coordinates

## Context

Product baseline roadmap theme "Next: OpenTelemetry" (`docs/initial-product-specs/rebalancer-product-baseline.md`).
Today the library only logs (US-011). The libraries target `netstandard2.1`, where `System.Diagnostics.Metrics` and
`ActivitySource` come from the `System.Diagnostics.DiagnosticSource` package: a new dependency, so this needs a
decision record (Tier 1) before implementation.

## Acceptance criteria

1. A `Meter` and an `ActivitySource`, both named `Rebalancer`, exist in `Rebalancer.Core`; no OpenTelemetry SDK
   dependency in any library package (hosts add their own exporter).
2. Counters per backend and group: rebalances started and completed, coordinator elections, assignment and
   unassignment events; a histogram of the time from unassignment to the next assignment.
3. One activity per rebalance on the coordinator, with the group, backend and fencing token (or epoch) as tags.
4. No resource names, connection strings or credentials in tags.
5. Unit tests with a `MeterListener` and an `ActivityListener` cover criteria 2–4 for SQL Server and Redis.

## Non-goals

- Exporters, dashboards or alert rules.
- ZooKeeper beyond the same counters (its rebalancing modes may need separate measures).

## Dependencies

- Decision records: a new ADR for the `System.Diagnostics.DiagnosticSource` dependency and the instrument names
  (not drafted yet); `docs/adrs/0002-…md`, `docs/adrs/0003-…md` (protocol events).
- Other stories: `docs/user-stories/US-011-pluggable-rebalancer-logging.md`

## Security & compliance

Telemetry leaves the process; criterion 4 keeps resource names (which may be sensitive, TM-0001 I1) and secrets out.

## Definition of ready

- [ ] Governing ADR referenced (or marked "N/A - non-architectural") – ADR still to be drafted
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/initial-product-specs/rebalancer-product-baseline.md` (Roadmap themes)
- `docs/user-stories/US-011-pluggable-rebalancer-logging.md`
- `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md`
