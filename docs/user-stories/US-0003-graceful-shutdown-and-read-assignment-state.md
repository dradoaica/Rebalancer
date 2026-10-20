---
id: US-0003
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Must"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0001", "US-0001" ]
---

# User story: Graceful shutdown and read assignment state

## Story

**As an** operator  
**I want** to stop the client within a timeout and to query current assignments and client state  
**So that** I can drain work safely and observe what the node believes it owns

## Context

`RebalancerClient` supports `StopAsync()` overloads with optional timeout and cancellation, `Dispose` with a bounded wait, `BlockAsync`, `GetAssignedResources`, and `GetCurrentState`. These align with operational shutdown and observability needs.

## Acceptance criteria

1. `StopAsync(TimeSpan)` cancels the internal `CancellationTokenSource` and waits up to the timeout for `WaitForCompletionAsync` to finish (per `RebalancerClient.cs`).
2. `GetAssignedResources()` delegates to `IRebalancerProvider.GetAssignedResources()` and returns `AssignedResources` suitable for display or assertions.
3. `GetCurrentState()` returns `ClientState` from the provider, or `NoProvider` if the provider reference is null (defensive branch in `RebalancerClient`).
4. `Dispose()` triggers cancellation and waits up to 5 seconds for provider completion.

## Non-goals

- Guaranteeing broker-side queue deletion or consumer tag cleanup (host responsibility).

## Dependencies

- Decision records: `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-0001-register-provider-and-join-resource-group.md`

## Security & compliance

N/A beyond ensuring shutdown does not log sensitive assignment payloads in shared environments (host policy).

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
- [x] Traceable to change record (if released) — `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md` (2.1.0), `docs/change-records/CR-0003-release-3-0-0.md` (3.0.0)

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Core/ClientState.cs`
- `src/Rebalancer.Core/AssignedResources.cs`
