---
id: ADR-0012
status: Proposed
date: 2026-10-18
decides: "entry point for constructing ZooKeeperProvider with a caller-supplied IZooKeeperService"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0003", "ADR-0011", "US-007" ]
supersedes: [ ]
superseded-by: ""
---

# Give a caller-supplied `IZooKeeperService` its own `ZooKeeperProvider` constructor

## Context and Problem Statement

ADR-0011 (Accepted) deprecates the ZooKeeper entry points without credentials in a 2.x minor release and removes them
in 3.0. One of those entry points is the main `ZooKeeperProvider` constructor:

```csharp
ZooKeeperProvider(string zookeeperHosts, string zooKeeperRootPath, TimeSpan sessionTimeout, TimeSpan connectTimeout,
    TimeSpan minimumRebalancingInterval, RebalancingMode rebalancingMode, IRebalancerLogger logger,
    IZooKeeperService zooKeeperService = null)
```

It is also the **only** way to pass a caller-supplied `IZooKeeperService` (for example a test double, or a service
with its own connection handling). With a custom service, `zookeeperHosts` is ignored and the service decides about
credentials and ACLs. Marking this constructor `[Obsolete]` would warn those callers with no alternative. Deleting it in
3.0 would leave them no way to inject a service. This record decides how a custom service is passed in.

## Decision Drivers

* ADR-0011's deprecation needs a non-deprecated path for every legitimate use.
* No overload that silently means "open ACL": credentials or an explicit opt-out stay visible in code (ADR-0011).
* Additive in 2.2.0 (no breaking change before 3.0).

## Considered Options

* **A. Add a constructor that takes the service first and no host string:**
  `ZooKeeperProvider(IZooKeeperService zooKeeperService, string zooKeeperRootPath, TimeSpan sessionTimeout,
  TimeSpan connectTimeout, TimeSpan minimumRebalancingInterval, RebalancingMode rebalancingMode, IRebalancerLogger logger)`,
  with `zooKeeperService` required (non-null). The service owns its credentials and ACLs. In 2.2.0 the old main
  constructor becomes `[Obsolete]`; in 3.0 it is removed.
* **B. Keep the old constructor, but deprecate only its use without a service.** C# cannot mark one argument value
  obsolete; this would need a runtime warning instead of a compiler warning.
* **C. Remove custom-service injection;** callers use the DI factory `AddRebalancerProvider(_ => …)` with the built-in
  service. That drops the test-double seam the integration tests and hosts may rely on.

## Decision Outcome

Proposed option: **A**. The strongest argument against A is that it adds a fourth constructor to a type that will lose
two in 3.0, and the two "service" and "hosts" constructors differ only by their first parameter type. That is easy to
document, and the compiler picks the right one from the argument type. B gives no compile-time signal, and C removes a
seam that exists today for a reason. The new constructor makes the trust decision explicit: whoever supplies the
service is responsible for its ACLs.

### Consequences

* Good, because ADR-0011's 2.2.0 deprecation can mark the old constructor `[Obsolete]` with a message that names both
  replacements (the ACL overload, and this constructor for a custom service).
* Good, because it is additive: no 2.x caller breaks.
* Bad, because a custom service bypasses ADR-0011's "credentials or explicit opt-out" rule. The XML documentation must
  say so, and TM-0001 should note it at its next update.

### Confirmation

* Unit test: the new constructor rejects a null service (`ArgumentNullException`) and works with `RebalancerClient`.
* Unit test (2.2.0): the old main constructor and `ZooKeeperService(string)` carry `[Obsolete]`; the ACL overloads and
  the new constructor do not.
* The integration tests that use the old constructor move to the ACL overload (with explicit `OPEN_ACL_UNSAFE` where
  they test the open behavior) or to this constructor.

## More Information

* Implements a prerequisite of [ADR-0011](0011-zookeeper-acls-by-default-in-next-major-version.md). Related:
  [ADR-0003](0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md), US-007.
* Release: 2.2.0 (additive + deprecation). The removal of the old constructor ships in 3.0 under ADR-0011.
