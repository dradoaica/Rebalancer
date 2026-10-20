---
id: US-0012
status: Done
date: 2026-04-11
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only (dradoaica, in session 2026-10-19, retroactive)
priority: "Should"
ssdlc-phase: Design
depends-on: [ "ADR-0001", "ADR-0005" ]
---

# User story: Register Rebalancer with dependency injection

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

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) — `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md` (2.1.0), `docs/change-records/CR-0003-release-3-0-0.md` (3.0.0)

## Sources

- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Providers.cs`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `src/Rebalancer.Extensions.DependencyInjection/`
- `tests/Rebalancer.UnitTests/`

## Note (2026-10-20)

`src/Rebalancer.Core/Providers.cs` was removed in 3.0 (ADR-0013). A provider is now registered through DI, as this story
describes, or passed to `RebalancerClient` (US-0015).
