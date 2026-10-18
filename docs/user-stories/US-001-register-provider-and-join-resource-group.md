---
id: US-001
status: Done
date: 2026-04-11
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Must"
ssdlc-phase: Maintenance     # as-built documentation
depends-on: [ "ADR-0001" ]
---

# User story: Register backend provider and join a resource group

## Story

**As a** library integrator  
**I want** to register exactly one `IRebalancerProvider` factory and start `RebalancerClient` for a named resource group  
**So that** my application participates in coordinated assignment using the backend I deployed (SQL Server, Redis, or ZooKeeper)

## Context

Hosts must call `Providers.Register` before constructing `RebalancerClient`, then `StartAsync(resourceGroup, clientOptions)` to bind to a group. This matches the public API in `Rebalancer.Core` and the sample in `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend`.

## Acceptance criteria

1. Given no prior successful `Providers.Register`, when `new RebalancerClient()` is used and the provider is resolved, then `Providers.GetProvider()` throws `ProviderException` with a clear message (per `Providers.cs`).
2. Given `Providers.Register(() => provider)` has been called once, when `RebalancerClient.StartAsync` is invoked with a non-null `ClientOptions`, then `IRebalancerProvider.StartAsync` is called with the same resource group string, `OnChangeActions`, cancellation token, and `ClientOptions`.
3. Documentation for this story cites `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md` as the governing decision for the registration pattern.

## Non-goals

- Supporting multiple concurrent provider registrations or different providers per resource group in one process.
- Container/DI integration beyond manual registration.

## Dependencies

- Decision records: `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories / services: N/A

## Security & compliance

Connection strings and ZooKeeper hosts are host-supplied secrets; this story does not add new secret storage. Document deployment responsibility: use environment-specific configuration and restrict network access to the backing store.

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
- [x] Traceable to change record (if released) — `docs/change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md` (2.1.0), `docs/change-records/CR-0002-release-3-0-0.md` (3.0.0)

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Providers.cs`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
- `README.md`
