---
id: US-002
status: Done
date: 2026-04-11
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Must"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0001", "US-001" ]
---

# User story: React to assignment, unassignment, and fatal abort

## Story

**As an** application developer  
**I want** `RebalancerClient` to raise events when resources are assigned, when work must stop, and when participation aborts  
**So that** I can start/stop consuming queues or other resources in lockstep with the coordinator

## Context

`RebalancerClient` exposes `OnAssignment`, `OnUnassignment`, and `OnAborted`. The provider invokes user actions through `OnChangeActions` (`OnStartActions`, `OnStopActions`, `OnAbortActions`). This models the README concept of coordinator-driven rebalancing.

## Acceptance criteria

1. When the provider signals a successful assignment with a list of resource identifiers, subscribers to `OnAssignment` receive `OnAssignmentArgs` containing those resources (see `RaiseOnAssignments` in `RebalancerClient.cs`).
2. When the provider signals stop activity, `OnUnassignment` is raised (via `StopActivity` / unassignment path).
3. When the provider signals abort with a reason and optional exception, `OnAborted` receives `OnAbortedArgs` with message and exception details.
4. Story dependencies reference ADR 0001 for how `OnChangeActions` connects providers to `RebalancerClient` events.

## Non-goals

- Defining application-level idempotency or message acknowledgment semantics (broker-specific).

## Dependencies

- Decision records: `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

Abort reasons may contain operational detail; avoid logging secrets from exception messages in production. N/A for data classification beyond host responsibility.

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

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Core/OnChangeActions.cs`
- `src/Rebalancer.Core/OnAssignmentArgs.cs`, `OnAbortedArgs.cs` (if present as separate files)
- `README.md`
