# User story: React to assignment, unassignment, and fatal abort

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-002                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

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

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Core/OnChangeActions.cs`
- `src/Rebalancer.Core/OnAssignmentArgs.cs`, `OnAbortedArgs.cs` (if present as separate files)
- `README.md`
