---
status: accepted
date: 2026-10-18
decision-makers: [dradoaica]
consulted: []
informed: []
---

# Warn when ZooKeeper ACLs are configured but operator-created znodes are still open

## Context and Problem Statement

ADR 0006 secures the znodes the provider creates. Operators usually create the group znode and its `resources` znode
themselves, to list the resources as children, and those znodes keep whatever ACL the operator gave them. The
coordinator stores the assignment table in the `resources` znode's data. A deployment can therefore enable ACLs and
still have world-writable assignments. `TM-0001` rates that as a **High** tampering risk, and the library currently
gives no signal about it. Today the only mitigation is a README note.

## Decision Drivers

* An operator who configured ACLs should learn that their assignments are still open, without reading the source.
* Do not break deployments that work today, and do not change znodes the library did not create.
* No new public API if possible.

## Considered Options

* **A. Log a warning.** When an auth scheme is configured, the provider reads the ACLs of `{group}` and
  `{group}/resources` once per session, after initializing its paths. It logs a warning through the configured
  `IRebalancerLogger` if any entry grants `world:anyone` write, create, delete or admin permission. Behavior is
  otherwise unchanged.
* **B. Fail.** Same check, but the provider aborts (`OnAborted`) instead of starting.
* **C. Fix it.** Same check, but the provider calls `setAcl` to apply its own ACL to those znodes.
* **D. Opt-in strict mode.** A new option chooses between A and B.

## Decision Outcome

Chosen option: **A**. The strongest argument against A is that a warning in a log is easy to miss, and the risk it
reports is High. B would make the misconfiguration impossible to miss, but it would also stop deployments that work
today the moment they upgrade, possibly for a group whose assignments are not sensitive. C changes znodes the operator
owns, can lock out the operator's own tooling, and needs admin permission that an open ACL happens to grant. D adds
public API for a choice B can still make later. A warning follows the deployment, costs one read per session, and
leaves room for B in the next major version (see ADR 0011).

### Consequences

* Good, because the misconfiguration becomes visible in the deployment's logs, with the znode path and the fix (point to
  `src/Rebalancer.ZooKeeper/README.md`).
* Good, because nothing changes for deployments without ACLs, and nothing is written to operator-owned znodes.
* Bad, because a host that uses `NullRebalancerLogger` (the default when no logger is given) never sees the warning.
  The README must recommend configuring a logger when ACLs are used.
* Bad, because there is one extra `getACL` round trip per znode per session. If reading an ACL fails with `NoAuth`, the
  provider's identity lacks read permission there, which does not prove the znode is closed to others. The provider then
  logs that the ACL could not be verified, rather than staying silent.

### Confirmation

* Integration test against the 3-node test cluster (`tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs`). A group
  whose `resources` znode was created with `OPEN_ACL_UNSAFE` logs exactly one warning naming that path. A group created
  with the group's credentials logs none. A provider without an auth scheme logs none.

## More Information

* Related: [ADR 0006](0006-provider-resource-ownership-and-zookeeper-acl.md),
  [ADR 0011](0011-zookeeper-acls-by-default-in-next-major-version.md),
  [TM-0001](../threat-models/TM-0001-zookeeper-coordination-and-acl.md) (Tampering T1, residual risks), US-007.
