# User story: Register Rebalancer with dependency injection

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-012                                             |
| **Status**      | Done                                               |
| **Priority**    | Should                                             |
| **Owner**       |                                                    |
| **SSDLC phase** | Design                                             |

## Story

**As a** host application author  
**I want** to register my chosen `IRebalancerProvider` and related options with `Microsoft.Extensions.DependencyInjection`  
**So that** I can use standard host startup, lifetimes, and test doubles without static `Providers.Register`

## Context

[ADR 0005](../adrs/0005-dependency-injection-for-provider-registration-v2.md) (**accepted**) records dual registration: `Rebalancer.Extensions.DependencyInjection` plus static `Providers` for backward compatibility.

## Acceptance criteria

1. Public API resolves `IRebalancerProvider` and `RebalancerClient` from `IServiceProvider` via `AddRebalancerSqlServer`, `AddRebalancerRedis`, `AddRebalancerZooKeeper`, `AddRebalancerProvider`, or `AddRebalancerClient` in `src/Rebalancer.Extensions.DependencyInjection/`.
2. Tests in `tests/Rebalancer.UnitTests` build a `ServiceProvider` and resolve `RebalancerClient` **without** `Providers.Register`; `RebalancerClient(IRebalancerProvider)` supports manual injection.
3. Root [README.md](../../README.md) documents DI registration alongside static `Providers` (no `[Obsolete]` on `Providers` in this milestone).

## Non-goals

- Supporting every third-party DI container beyond Microsoft.Extensions.DependencyInjection in the first iteration.
- Removing `Providers` in the same milestone unless explicitly scoped.

## Dependencies

- Decision records: `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md` (**accepted**)
- Other stories: N/A

## Security & compliance

Connection strings and secrets remain in options bound from configuration; no new secret storage in the library.

## Definition of done

- [x] Acceptance criteria met
- [x] Tests / checks agreed with team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Providers.cs`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Extensions.DependencyInjection/`
- `tests/Rebalancer.UnitTests/`
