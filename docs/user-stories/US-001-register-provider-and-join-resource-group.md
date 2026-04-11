# User story: Register backend provider and join a resource group

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-001                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

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

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- `src/Rebalancer.Core/Providers.cs`
- `src/Rebalancer.Core/RebalancerClient.cs`
- `examples/Rebalancer.RabbitMq.ExampleWithSqlServerBackend/Program.cs`
- `README.md`
