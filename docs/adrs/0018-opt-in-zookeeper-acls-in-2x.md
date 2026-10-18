---
id: ADR-0018
status: Accepted
date: 2026-10-18
decides: "ZooKeeper ACL support in the 2.x releases"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0003", "ADR-0006", "US-007" ]
supersedes: [ "ADR-0006" ]
superseded-by: ""
---

# Opt-in ZooKeeper ACLs in 2.x

## Context and Problem Statement

This record carries over ADR-0006 item 4 with ADR-0006's chosen option B, **unchanged**, as part of splitting ADR-0006
into one-concern records (with ADR-0016 and ADR-0017). It introduces no new decision.

ZooKeeper znodes were created with `OPEN_ACL_UNSAFE`, so anyone who can reach the ensemble can rewrite assignments.

## Decision Drivers

* Make ACLs available to `ZooKeeperProvider` users.
* Stay binary-compatible in 2.x.

## Considered Options

* **A. Change the `ZooKeeperService` constructor signature to add ACL parameters** (ADR-0006 option A; binary-breaking).
* **B. Keep the old constructor as an overload and add ACL overloads** on `ZooKeeperService`, `ZooKeeperProvider` and
  `AddRebalancerZooKeeper` (ADR-0006 option B).

## Decision Outcome

Proposed option: **B**, as already in force since 2.1.0. With credentials and no explicit ACL, znodes are created with
`CREATOR_ALL_ACL`; without credentials the default stays `OPEN_ACL_UNSAFE`. The strongest argument against B: it adds
overloads to keep compatibility for a constructor few callers use directly. The constructor is public API, and the ACL
is only useful if `ZooKeeperProvider` users can set it.

### Consequences

* Good, because 2.x deployments can secure their znodes without a breaking change.
* Bad, because ACL misconfiguration (e.g., `CREATOR_ALL_ACL` with different credentials per node) can make nodes unable
  to modify each other's znodes; operator notes in `src/Rebalancer.ZooKeeper/README.md`.

### Confirmation

* `tests/Rebalancer.UnitTests/ZooKeeperAclApiTests.cs`, `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs`
  (existing).

## More Information

* Replaces item 4 of [ADR-0006](0006-provider-resource-ownership-and-zookeeper-acl.md); see
  [ADR-0016](0016-rebalancer-client-owns-its-provider.md) for how ADR-0006 is marked superseded.
* Scope is 2.x only. The policy from 3.0 is [ADR-0011](0011-zookeeper-acls-by-default-in-next-major-version.md); the
  open-znode warning is [ADR-0010](0010-warn-on-open-operator-znodes-when-acls-are-configured.md).
* No code change.
