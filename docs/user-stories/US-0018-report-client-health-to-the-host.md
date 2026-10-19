---
id: US-0018
status: Draft
date: 2026-10-19
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Could"
ssdlc-phase: Planning
depends-on: [ "ADR-0005", "US-003", "US-012" ]
---

# User story: Report a Rebalancer client's health to the host

## Story

**As an** operator of a host that runs a Rebalancer client  
**I want** the host's health endpoint to report when the client has aborted or cannot reach its backend  
**So that** my orchestrator restarts or alerts on a node that silently stopped taking part in its group

## Context

Product baseline roadmap theme "Next: health checks". `RebalancerClient.GetCurrentState()` already exposes
`ClientState` (US-003), and `OnAborted` signals a non-recoverable error. A health check that adapts these to
`Microsoft.Extensions.Diagnostics.HealthChecks` needs that package: a new dependency, so a decision record first
(Tier 1). A host can already write the same check itself in a few lines; the story is only worth doing if several
hosts need it.

## Acceptance criteria

1. `AddRebalancerHealthCheck()` registers a check for the DI-registered client (US-012).
2. `Unhealthy` in state `Aborted`, or `Stopped` while the host runs; `Degraded` while `PendingAssignment`
   lasts longer than a configured period (default: 2 × the group's lease expiry); `Healthy` otherwise.
3. The check never blocks: it reads state only (ADR-0007), with no call to the backend.
4. Unit tests cover each state mapping.

## Non-goals

- Checking the backend server itself (Redis, SQL Server, ZooKeeper have their own health checks).

## Dependencies

- Decision records: a new ADR for the `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` dependency (not
  drafted yet); `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- Other stories: `docs/user-stories/US-003-graceful-shutdown-and-read-assignment-state.md`,
  `docs/user-stories/US-012-register-rebalancer-with-dependency-injection.md`

## Security & compliance

Health output must not include resource names or connection details; status and a short reason only.

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
- `src/Rebalancer.Core/RebalancerClient.cs` (`GetCurrentState`, `OnAborted`), `src/Rebalancer.Core/ClientState.cs`
