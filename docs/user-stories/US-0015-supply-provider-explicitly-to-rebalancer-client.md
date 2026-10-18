---
id: US-0015
status: Draft
date: 2026-10-18
owner: ""
approved-by: [ ]            # readiness approval; set by a human only
priority: "Must"
ssdlc-phase: Coding
depends-on: [ "ADR-0013", "ADR-0005", "US-001", "US-012" ]
---

# User story: Supply a provider explicitly to a Rebalancer client

## Story

**As a** library integrator
**I want** to create a `RebalancerClient` with the provider for my backend, directly or through dependency injection
**So that** my application joins a resource group without any process-wide registration

## Context

US-001 described joining a resource group through the static `Providers.Register` registry. ADR-0013 removes that
registry in 3.0 because its process-wide state caused a real defect (PRB-2026-10-18-zookeeper-tests-ignored-their-provider).
This story describes the 3.0 way and succeeds US-001 for 3.0 and later.

## Acceptance criteria

1. `new RebalancerClient(provider)` uses exactly the given provider; `new RebalancerClient(null)` throws
   `ArgumentNullException`.
2. `Rebalancer.Core` has no `Providers` type and no parameterless `RebalancerClient` constructor
   (`DeprecationContractTests.StaticProviderRegistry_IsRemoved`).
3. `AddRebalancerSqlServer`, `AddRebalancerRedis`, `AddRebalancerZooKeeper` and `AddRebalancerProvider` resolve a
   `RebalancerClient` with a new provider per client (`DiRegistrationTests`).
4. The example application and the README create clients with an explicit provider.

## Non-goals

- Any registry or service-locator replacement for `Providers`.

## Dependencies

- Decision records: `docs/adrs/0013-explicit-providers-only-remove-static-registry.md`,
  `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md` (predecessor),
  `docs/user-stories/US-012-register-rebalancer-with-dependency-injection.md`

## Security & compliance

No change: connection strings and credentials are passed by the host as before, from its own configuration.

## Definition of ready

- [ ] Governing ADR referenced (or marked "N/A - non-architectural")
- [ ] Acceptance criteria are testable, unambiguous, and numbered
- [ ] Security & compliance considerations addressed
- [ ] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- `docs/adrs/0013-explicit-providers-only-remove-static-registry.md`
- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- `src/Rebalancer.Core/RebalancerClient.cs`, `src/Rebalancer.Extensions.DependencyInjection/`
