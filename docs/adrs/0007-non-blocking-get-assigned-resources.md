---
id: ADR-0007
status: Accepted
date: 2026-10-18
decides: "blocking behavior of GetAssignedResources() in the SQL Server and Redis providers"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0001", "US-0003" ]
supersedes: [ ]
superseded-by: [ ]
---

# Make `GetAssignedResources()` non-blocking in the SQL Server and Redis providers

## Context and Problem Statement

`RebalancerClient.GetAssignedResources()` is documented as: "If the client is pending assignment or not in an active
state then the resources collection will be empty." The backends disagree:

| Provider            | Behavior while assignment is pending                                            |
|---------------------|---------------------------------------------------------------------------------|
| `ZooKeeperProvider` | Returns immediately with the current (empty) resources, as documented           |
| `SqlServerProvider` | Blocks the calling thread (`while (true)` + `Thread.Sleep(100)`) until assigned |
| `RedisProvider`     | Same as SQL Server                                                              |

If a SQL Server or Redis node is never assigned (for example, the resource group does not exist and the node aborts),
the call never returns. On 2026-10-18 this made `RedisAssignmentSafetyTests` hang instead of failing; the tests now
work around it by calling the method only when `GetCurrentState()` is `Assigned`.

Both providers also have a public `GetAssignedResources(CancellationToken)` overload that stops waiting when cancelled.
It is not on `IRebalancerProvider`, so `RebalancerClient` cannot use it.

## Decision Drivers

* One documented contract for `IRebalancerProvider`, whichever backend is used.
* No call that can block a thread forever.
* No surprise for existing callers that rely on the current blocking behavior.

## Considered Options

* **A. Make SQL Server and Redis match the documented, non-blocking contract**: return the current state at once,
  with empty resources while pending, as ZooKeeper does. Keep the `CancellationToken` overloads for callers who want to wait.
* **B. Make blocking the documented contract**, and add a bounded wait (`GetAssignedResources(TimeSpan)` on
  `RebalancerClient`, with a default interface method on `IRebalancerProvider`); ZooKeeper would then need to block too.
* **C. Keep things as they are** and only document that SQL Server and Redis block.

## Decision Outcome

Chosen option: **A**. The strongest argument against A: a caller of the SQL Server or Redis provider may be relying
on the wait. For example, it may call `GetAssignedResources()` right after `StartAsync` and expect resources back. With
A that caller would get an empty list and `PendingAssignment`. Such a caller is already relying on behavior that
contradicts the documentation and doesn't hold for ZooKeeper, and the `CancellationToken` overload stays available
for an explicit wait. The example application calls it inside `OnAssignment`, where the client is already assigned, so
it is unaffected.

### Consequences

* Good, because all three backends behave the same and as documented, and no call can hang forever.
* Bad, because it is a behavior change for SQL Server and Redis callers that wait implicitly; release notes must say so.

### Confirmation

* A unit or integration test per backend: with no assignment, `GetAssignedResources()` returns at once with
  `PendingAssignment` and no resources.
* The workaround in `RedisAssignmentSafetyTests` / `SqlServerAssignmentSafetyTests` can then be removed.

## More Information

* Related: [ADR 0001](0001-pluggable-rebalancer-provider-and-client-lifecycle.md) (provider contract), US-0003.
