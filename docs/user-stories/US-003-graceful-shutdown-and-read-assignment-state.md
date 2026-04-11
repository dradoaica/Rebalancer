# User story: Graceful shutdown and read assignment state

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-003                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

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
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

N/A beyond ensuring shutdown does not log sensitive assignment payloads in shared environments (host policy).

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Core/ClientState.cs`
- `src/Rebalancer.Core/AssignedResources.cs`
