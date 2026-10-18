---
id: ADR-0011
status: Accepted
date: 2026-10-18
decides: "default ZooKeeper ACL policy from version 3.0"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0006", "ADR-0010", "TM-0001", "US-007" ]
supersedes: [ ]
superseded-by: ""
---

# Require ZooKeeper credentials by default in the next major version (3.0)

## Context and Problem Statement

ADR 0006 made ZooKeeper ACLs opt-in to keep 2.x compatible. The default stays `OPEN_ACL_UNSAFE`, so every deployment
that does not opt in can have its assignments rewritten, its leader election spoofed, and its rebalancing stalled by any
client that can reach the ensemble. `TM-0001` rates those risks **High** and lists "ACLs stay opt-in" as a residual
risk. The next major version is the place to make the secure setting the default.

ZooKeeper's `CREATOR_ALL_ACL` needs an authenticated session. "Secure by default" therefore means requiring credentials
(or an explicit opt-out), not silently switching an ACL.

## Decision Drivers

* Secure default: a deployment that changes nothing should not be world-writable.
* An explicit, visible opt-out for environments where the network is the only control (e.g., local development).
* A migration path announced before the breaking release.

## Considered Options

* **A. Deprecate in 2.x, require in 3.0.**
  * In a 2.x minor, mark the constructors and DI overload without credentials `[Obsolete]`, with a message pointing to
    the ACL overloads.
  * In 3.0, remove them; the remaining entry points require either credentials or an explicit
    `ZooDefs.Ids.OPEN_ACL_UNSAFE` ACL (the opt-out).
* **B. Keep ACLs opt-in** (ADR 0006 as is) and rely on ADR 0010's warning.
* **C. Default to an IP-based ACL** (`ip:` scheme) built from the node's address. This breaks behind NAT and in
  containers, and gives no protection on shared networks.

## Decision Outcome

Chosen option: **A**. The strongest argument against A is that it breaks every existing ZooKeeper caller in 3.0 and
pushes credentials management onto users who may rely on network isolation and consider it enough. Network isolation
is a single control, and TM-0001 shows that one reachable client is enough to break the safety invariant. An explicit
`OPEN_ACL_UNSAFE` keeps those users working with a one-line change that also documents their choice in code. The 2.x
deprecation gives at least one release of warnings first.

This decision does not change 2.x behavior. It supersedes ADR 0006 only for 3.0 and later, where ACLs stop being opt-in.

### Consequences

* Good, because new 3.0 deployments are protected unless they explicitly opt out.
* Good, because the opt-out is visible in code review.
* Bad, because 3.0 is a breaking change for every ZooKeeper caller (release notes, migration guide).
* Bad, because the `[Obsolete]` warnings in 2.x can break builds that treat warnings as errors.

### Confirmation

* 2.x minor: unit tests assert the `[Obsolete]` attributes on the constructors and DI overload without credentials.
* 3.0: the binary-compatibility test `ZooKeeperAclApiTests.PreAclConstructors_StillExist_ForBinaryCompatibility` is
  replaced by a test that the constructors without credentials are gone. A change record carries the migration guide.

## More Information

* Related: [ADR 0006](0006-provider-resource-ownership-and-zookeeper-acl.md) (superseded for 3.0+ on this point),
  [ADR 0010](0010-warn-on-open-operator-znodes-when-acls-are-configured.md),
  [TM-0001](../threat-models/TM-0001-zookeeper-coordination-and-acl.md), US-007.
