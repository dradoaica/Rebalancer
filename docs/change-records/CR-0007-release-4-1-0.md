---
id: CR-0007
status: Approved
date: 2026-10-19
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.1.0"
depends-on: [ "ADR-0022", "ADR-0023", "US-0017", "US-0018", "US-0019", "CR-0006" ]
---

# Change record: Rebalancer 4.1.0 – telemetry, health check, randomised tests

## Summary

Releases 4.1.0 of the five packages and the new `Rebalancer.Extensions.Diagnostics.HealthChecks`. Adds metrics and
traces (ADR-0022, US-0017), a health check package (ADR-0023, US-0018), and randomised SQL Server / Redis tests
(US-0019). Additive only. Release notes: `docs/release-notes/4.1.0.md`.

## Motivation / drivers

- Product baseline roadmap "Next": OpenTelemetry, health checks, randomised parity with the ZooKeeper tests.

## Scope

**In scope:**

- `src/Rebalancer.Core/RebalancerTelemetry.cs` (new), `RebalancerClient.cs` (assignment counters, unassigned
  duration); direct reference `System.Diagnostics.DiagnosticSource` 10.0.13 (already resolved transitively);
  `InternalsVisibleTo` for the three backends.
- SQL Server and Redis `Roles/Coordinator.cs` (rebalance activity and outcome) and providers (elections); ZooKeeper
  coordinators (rebalance activity and outcome; internal `ResourceGroup` property, no public API change) and provider
  (elections).
- New package `src/Rebalancer.Extensions.Diagnostics.HealthChecks` (references
  `Microsoft.Extensions.Diagnostics.HealthChecks` and `.Abstractions` 10.0.13); added to `Rebalancer.slnx`.
- Tests: `RebalancerClientTelemetryTests`, `RebalancerClientHealthCheckTests` (unit); `*TelemetryTests`,
  `*RandomisedTests` (integration, Docker); shared `tests/Shared/TelemetryRecorder.cs`.
- Versions 4.1.0; release notes; README; architecture overview; stories US-0017..0019.

**Out of scope:**

- Making SQL Server and Redis report `ClientState.Aborted` (ADR-0023, More Information).
- ZooKeeper telemetry tests (need a ZooKeeper ensemble; code paths covered by build and inspection only).

## Impact assessment

| Area                  | Impact                                                                                                   |
|-----------------------|----------------------------------------------------------------------------------------------------------|
| Users / customers     | Opt-in: telemetry only when a listener (OpenTelemetry) subscribes; the health check only when registered. |
| Services / components | One counter / activity call per event; no cost without a listener.                                       |
| Data / schema         | None.                                                                                                    |
| Security              | Group names leave the process in telemetry (ADR-0022); resource names never do.                          |
| Operations            | None. Any upgrade order; mixed 4.0 / 4.1 groups behave as 4.0.                                           |

## Preconditions

- [x] Decision records in place: ADR-0022 and ADR-0023 (Accepted by dradoaica, 2026-10-19; on the shared trunk with
  this release's merge).
- [x] Readiness approval (`approved-by`) of US-0017, US-0018 and US-0019 recorded (dradoaica, 2026-10-19).
- [x] Tests on branch `agent/release-4.1.0`: see the evidence line below.
- [ ] Rollback procedure validated: redeploy 4.0.0 (no data change); not rehearsed.
- [x] Pre-change test suite passing cleanly.

Evidence (2026-10-19): all six libraries build with warnings as errors; unit 56/56; SQL Server + Redis integration
20/20 (Docker), including both randomised runs (3 minutes each). ZooKeeper integration not run (no ensemble); the
ZooKeeper telemetry is covered by build and inspection only. No other package references the health-check packages.

## Implementation plan

1. Review branch `agent/release-4.1.0`; merge into `main` and push. The push publishes 4.1.0, including the new
   package; merging is a human action, and the pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" workflow run succeeds, including "Push to GitHub Packages", and lists six packages.

## Rollback plan

Redeploy 4.0.0 and remove the health-check package reference; no data or configuration to undo.

## Communications

Release notes `docs/release-notes/4.1.0.md`; README sections "Metrics and traces" and "Health check".

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-10-19 |
| Release / CAB (or org) | dradoaica | 2026-10-19 |

## Post-implementation

- **Actual outcome:** {to be filled after the release}
- **Deviations:** {…}
- **Follow-ups:** {…}

## Sources

- ADRs: `docs/adrs/0022-rebalancing-telemetry-contract.md`, `docs/adrs/0023-health-check-for-a-rebalancer-client.md`
- User stories: `docs/user-stories/US-0017-emit-rebalancing-metrics-and-traces.md`,
  `docs/user-stories/US-0018-report-client-health-to-the-host.md`,
  `docs/user-stories/US-0019-randomised-assignment-safety-tests-for-sql-server-and-redis.md`
- Previous release: `docs/change-records/CR-0006-release-4-0-0.md`
