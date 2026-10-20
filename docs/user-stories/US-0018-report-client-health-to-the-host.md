---
id: US-0018
status: Done
date: 2026-10-19
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19)
priority: "Could"
ssdlc-phase: Coding
depends-on: [ "ADR-0023", "ADR-0005", "US-0003", "US-0012" ]
---

# User story: Report a Rebalancer client's health to the host

## Story

**As an** operator of a host that runs a Rebalancer client  
**I want** the host's health endpoint to report when the client has aborted or cannot reach its backend  
**So that** my orchestrator restarts or alerts on a node that silently stopped taking part in its group

## Context

Product baseline roadmap theme "Next: health checks". `RebalancerClient.GetCurrentState()` already exposes
`ClientState` (US-0003), and `OnAborted` signals a non-recoverable error. A health check that adapts these to
`Microsoft.Extensions.Diagnostics.HealthChecks` needs those packages: a new dependency, decided in ADR-0023 (Accepted;
an optional package). Drafting the ADR showed that `RebalancerClient` is registered transient (no single DI client to
check), that SQL Server and Redis never report `Aborted`, and that the client does not expose the lease expiry; the
criteria below reflect that.

## Acceptance criteria

1. `AddRebalancerClient(name, client, pendingThreshold)` on `IHealthChecksBuilder` registers a check for the host's
   own client instance (registration is transient, so the check cannot resolve it from DI).
2. `Unhealthy` after `OnAborted` (needed for SQL Server and Redis) or in state `Aborted`, `Stopped`, `NotStarted` or
   `NoProvider`; `Degraded` while `PendingAssignment` lasts longer than the configured threshold; `Healthy` otherwise.
3. The check never blocks: it reads state only (ADR-0007), with no call to the backend.
4. Unit tests cover each state mapping.

## Non-goals

- Checking the backend server itself (Redis, SQL Server, ZooKeeper have their own health checks).

## Dependencies

- Decision records: `docs/adrs/0023-health-check-for-a-rebalancer-client.md` (Accepted);
  `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- Other stories: `docs/user-stories/US-0003-graceful-shutdown-and-read-assignment-state.md`,
  `docs/user-stories/US-0012-register-rebalancer-with-dependency-injection.md`

## Security & compliance

Health output must not include resource names or connection details; status and a short reason only.

## Definition of ready

- [x] Governing ADR referenced (or marked "N/A - non-architectural") – ADR-0023 (Accepted 2026-10-19)
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met – `src/Rebalancer.Extensions.Diagnostics.HealthChecks/`; `RebalancerClientHealthCheckTests`
  (each state, the abort while `Assigned`, no backend call, registration)
- [x] Tests / checks agreed with a team (dradoaica, 2026-10-19)
- [x] Docs / runbooks updated if needed – release notes 4.1.0, README
- [x] Traceable to change record (if released) - `docs/change-records/CR-0007-release-4-1-0.md`

## Sources

- `docs/adrs/0023-health-check-for-a-rebalancer-client.md`
- `docs/initial-product-specs/rebalancer-product-baseline.md` (Roadmap themes)
- `src/Rebalancer.Core/RebalancerClient.cs` (`GetCurrentState`, `OnAborted`), `src/Rebalancer.Core/ClientState.cs`
