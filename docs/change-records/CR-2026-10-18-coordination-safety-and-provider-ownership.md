---
id: CR-2026-10-18-coordination-safety-and-provider-ownership
status: Implemented
date: 2026-10-18
owner: "dradoaica"
approved-by: [ "dradoaica" ]
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 2.1.0"
depends-on: [ "ADR-0002", "ADR-0003", "ADR-0006", "ADR-0007", "ADR-0008", "ADR-0009", "ADR-0010", "TM-0001", "PRB-2026-10-18-demoted-coordinator-keeps-resources", "PRB-2026-10-18-zookeeper-tests-ignored-their-provider", "US-001", "US-003", "US-005", "US-006", "US-007", "US-010", "US-011", "US-012", "US-013" ]
---

# Change record: Coordination safety fixes, provider ownership, and ZooKeeper ACLs

## Summary

Releases the 2026-10-18 batch of the Rebalancer libraries:

- A safety fix: a demoted SQL Server or Redis coordinator could keep running its resources.
- The Redis lease and client-state rework: server time and compare-and-set writes.
- SQL Server transaction and connection fixes.
- ZooKeeper robustness fixes.
- Provider ownership: `RebalancerClient` now disposes the provider it was given.
- Opt-in ZooKeeper ACLs (ADR 0006, option B).
- `GetAssignedResources()` no longer blocks in the SQL Server and Redis providers (ADR 0007).
- The unused `StackExchange.Redis.DataTypes` package is replaced by a direct `Newtonsoft.Json` 13.0.3 reference,
  removing the vulnerable transitive `Newtonsoft.Json` 12.0.1 (ADR 0008).
- A second `Providers.Register` call with a different factory now throws instead of being silently ignored (ADR 0009).

The library API stays source- and binary-compatible: every pre-existing public signature is kept, and new behavior is
added as overloads. Three documented behavior changes remain: provider ownership (ADR 0006), the non-blocking
`GetAssignedResources()` (ADR 0007), and rejected conflicting registrations (ADR 0009).

## Motivation / drivers

- Assignment safety (never two nodes on one resource): `docs/problem-records/PRB-2026-10-18-demoted-coordinator-keeps-resources.md`.
- Lease correctness independent of client clock skew; no lost updates between concurrent Redis writers.
- No Redis connection leak per disposed client.
- ZooKeeper znodes were always world-writable (`OPEN_ACL_UNSAFE`).
- `GetAssignedResources()` could block a thread forever on SQL Server and Redis, contrary to its documentation.
- Known high-severity advisory GHSA-5crp-9r3c-p9vr in the transitive `Newtonsoft.Json` 12.0.1.

## Scope

**In scope:**

- `src/Rebalancer.Core`: `RebalancerClient` stop/dispose lifecycle; `Providers.Register` conflict check (ADR 0009).
- `src/Rebalancer.Redis`: `RedisHash`, client, lease and resource services, coordinator role, `RedisProvider : IDisposable`,
  non-blocking `GetAssignedResources()`, package references (ADR 0008).
- `src/Rebalancer.SqlServer`: non-blocking `GetAssignedResources()` (ADR 0007).
- `src/Rebalancer.SqlServer`: coordinator role, client start transaction, lease query, connection retry.
- `src/Rebalancer.ZooKeeper`: ACL/auth overloads, null-logger fallback, session/cancellation fixes.
- `src/Rebalancer.Extensions.DependencyInjection`: `AddRebalancerZooKeeper` ACL overload.

**Out of scope:**

- Database schema changes (none in this batch).

## Impact assessment

| Area                  | Impact                                                                                                                                                                                                                |
|-----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Callers that share one provider instance between clients, or keep using a provider after disposing its client, must create one provider per client (ADR 0006). DI registrations already do. SQL Server / Redis callers that relied on `GetAssignedResources()` waiting for an assignment get `PendingAssignment` and no resources instead; they can use the provider's `GetAssignedResources(CancellationToken)` overload to wait (ADR 0007). Consumers that used `RedisDictionary` / `RedisList` through Rebalancer's transitive reference must reference `StackExchange.Redis.DataTypes` themselves (ADR 0008). Applications that call `Providers.Register` with two different factories now fail at that call; they must register once, or pass providers explicitly (ADR 0009). |
| Services / components | During a rebalance the coordinator now reports `PendingAssignment` instead of its old resources, like followers already did.                                                                                     |
| Data / schema         | No schema change. Redis keys and JSON encoding unchanged (`Dictionary:` / `List:` keys, Newtonsoft JSON), so existing data stays readable.                                                                         |
| Security              | ZooKeeper ACLs become available but stay opt-in; the default is unchanged (`OPEN_ACL_UNSAFE`). Credentials passed as `authData` are secrets. The published Redis package no longer pulls `Newtonsoft.Json` 12.0.1 (GHSA-5crp-9r3c-p9vr). |
| Operations            | **Redis groups need an all-nodes-at-once upgrade** (see Implementation plan). SQL Server and ZooKeeper groups can be upgraded node by node.                                                                        |

## Preconditions

- [x] Decision records in place if required: `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md`,
  `docs/adrs/0007-non-blocking-get-assigned-resources.md`, `docs/adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md`,
  `docs/adrs/0009-reject-conflicting-provider-registrations.md` (all Accepted)
- [x] Tests / dry-run completed (2026-10-18, on the working tree that became the release commit): unit tests 27/27;
  Redis and SQL Server integration 2/2; ZooKeeper single-client, multi-client and ACL 22/22 against a 3-node ensemble;
  randomised ZooKeeper smoke run (`REBALANCER_RANDOMISED_TEST_MAX_MINUTES=2`) 6/13 run, all passed, then stopped as
  sufficient by dradoaica. A re-run on the commit itself was waived by dradoaica.
- [ ] Rollback procedure validated / dry-run tested — not performed; redeploying 2.0.3 needs no data migration, but the
  procedure has not been rehearsed
- [x] Pre-change test suite passing cleanly (same runs as above; build without new warnings)

## Implementation plan

1. Merge the reviewed change set and tag the release; publish the NuGet packages as version 2.1.0.
2. Update consumers one resource group at a time.
3. **Redis groups:** stop every node of the group, deploy the new version, then start the nodes. Old nodes compare lease
   times with their own clock and write without compare-and-set. In a mixed group, lease expiry would be judged
   against two different clocks, and concurrent updates could be lost.
4. **SQL Server and ZooKeeper groups:** rolling upgrade, one node at a time.
5. **Optional, ZooKeeper ACLs:** only after every node runs the new version. Recreate the resource group (or `setAcl` its
   existing znodes), then start all nodes with the same credentials. See `src/Rebalancer.ZooKeeper/README.md`.

## Validation plan

- After each group upgrade: every node reaches `Assigned`, all resources are assigned, and no resource is reported by
  two nodes (same invariant as `RedisAssignmentSafetyTests` / `SqlServerAssignmentSafetyTests`).
- Redis: the number of open connections no longer grows as clients are created and disposed.
- ZooKeeper with ACLs: a session without the credentials gets `NoAuth` on the group's `clients` znode.

## Rollback plan

- Redeploy the previous package version, with the same all-nodes-at-once rule for Redis groups. No data migration is
  needed, because the stored format did not change.
- If ZooKeeper ACLs were enabled, previous versions cannot set credentials and will get `NoAuth`. Before rolling back,
  reset the group's znodes to `world:anyone` with `setAcl` (as the credentialed identity) or recreate the group.
- Executed by the service owner of each consuming application.

## Communications

Release notes: `docs/release-notes/2.1.0.md`, listing the ownership change (ADR 0006), the non-blocking `GetAssignedResources()` (ADR 0007), the
dependency change (ADR 0008), the rejected conflicting registrations (ADR 0009), the Redis all-nodes-at-once upgrade
rule, and the opt-in ACL feature. Version 2.1.0 is a minor version despite these behavior changes; the release notes
must list them as potentially breaking. Notify the owners of consuming applications before publishing.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-10-18 |
| Release / CAB (or org) | dradoaica | 2026-10-18 |

Approved for execution on 2026-10-18 (one person holds both roles). The unticked preconditions above must be met on
the release commit before step 1 of the implementation plan.

## Amendment 1 (2026-10-18) – approved

Added after this record was approved, and approved separately on 2026-10-18 for release with 2.1.0.

- **Change:** ADR 0010 (Accepted). With credentials configured, the ZooKeeper provider logs a warning for each
  operator-created znode (`{group}`, `{group}/resources`) that anyone can change, or whose ACL it cannot read. It is an
  internal check on `ZooKeeperService`, with no public API change and no change to znodes.
- **Impact:** one `getACL` call per znode per session, only when credentials are configured; new warnings in the
  logs of deployments whose operator-created znodes are open.
- **Validation:** `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs` (open znodes reported once each in both
  modes; secured znodes not reported; no check without credentials).
- **Rollback:** as for the rest of this record (previous package version).
- **Not in this release:** ADR 0011 (Accepted) is scheduled for a later 2.x minor (deprecation) and 3.0 (removal).

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-10-18 |
| Release / CAB (or org) | dradoaica | 2026-10-18 |

## Post-implementation

- **Actual outcome (2026-10-18):** commit `2952de6` merged into `main` (fast-forward) and pushed by dradoaica. The
  "Release Packages" workflow ran on that push and completed successfully at 13:15:17 UTC, including the
  "Push to GitHub Packages" step (https://github.com/dradoaica/Rebalancer/actions/runs/39130690089). The published package versions were not listed from the agent session
  (the GitHub Packages API requires authentication).
- **Deviations:** the rollback dry-run was not performed (see Preconditions). No consumer upgrades are recorded here;
  they follow the implementation plan per resource group.
- **Follow-ups (done 2026-10-18):** both problem records closed; "Traceable to change record" ticked in US-001,
  US-003, US-005, US-006, US-007, US-010, US-011, US-012 and US-013.

## Sources

- User stories: `docs/user-stories/US-001-…`, `US-003-…`, `US-005-…`, `US-006-…`, `US-007-…`, `US-010-…`, `US-011-…`, `US-012-…`,
  `US-013-…`
- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`,
  `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md`, `docs/adrs/0007-non-blocking-get-assigned-resources.md`,
  `docs/adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md`,
  `docs/adrs/0009-reject-conflicting-provider-registrations.md`
- Problem records: `docs/problem-records/PRB-2026-10-18-demoted-coordinator-keeps-resources.md`,
  `docs/problem-records/PRB-2026-10-18-zookeeper-tests-ignored-their-provider.md`
- SDP: N/A (library, no service offering)
- Accepted threat model: `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md` (Accepted 2026-10-18)
