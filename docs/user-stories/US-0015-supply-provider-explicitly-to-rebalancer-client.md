---
id: US-0015
status: Done
date: 2026-10-18
owner: ""
approved-by: [ "dradoaica" ]            # readiness approval; set by a human only
priority: "Must"
ssdlc-phase: Coding
depends-on: [ "ADR-0013", "ADR-0005", "US-001", "US-012", "CR-0002" ]
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
   `RebalancerClient` with a new provider per client (`DiRegistrationTests.EachResolvedClient_GetsItsOwnProvider`).
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

<!-- 2026-10-18: items verified by an agent against this story's text; the human readiness review is recorded by
setting status and approved-by. Note: the implementation was authorized by ADR-0013 (Tier 1) and shipped in 3.0.0
before this story was reviewed. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met", "Docs" and change-record traceability verified by an agent against the code,
the tests and the 3.0.0 release; "Tests / checks agreed with a team" needs a human. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [x] Traceable to change record (if released) - `docs/change-records/CR-0002-release-3-0-0.md` (3.0.0)

## Sources

- `docs/adrs/0013-explicit-providers-only-remove-static-registry.md`
- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md`
- `src/Rebalancer.Core/RebalancerClient.cs`, `src/Rebalancer.Extensions.DependencyInjection/`
