---
id: CR-0002
status: Implemented
date: 2026-10-18
owner: "dradoaica"
approved-by: [ "dradoaica" ]          # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 2.2.0"
depends-on: [ "ADR-0011", "ADR-0012", "ADR-0013", "US-001", "US-007", "US-012", "TM-0001" ]
---

# Change record: Rebalancer 2.2.0 – deprecations ahead of 3.0

## Summary

Releases 2.2.0 of all five packages. It adds the `ZooKeeperProvider(IZooKeeperService, …)` constructor (ADR-0012), and
marks as `[Obsolete]` the APIs that 3.0 removes: the static `Providers` registry and `RebalancerClient()` (ADR-0013),
and the ZooKeeper entry points without credentials (ADR-0011). There is no runtime behavior change and no data change.
Release notes: `docs/release-notes/2.2.0.md`.

## Motivation / drivers

- ADR-0011 and ADR-0013 require at least one release with compiler warnings before 3.0 removes these APIs.
- ADR-0012 gives callers of a custom `IZooKeeperService` a non-deprecated path, so the ADR-0011 deprecation leaves no
  legitimate use without an alternative.

## Scope

**In scope:**

- `src/Rebalancer.Core`: `[Obsolete]` on `Providers` and `RebalancerClient()`.
- `src/Rebalancer.ZooKeeper`: new constructor; `[Obsolete]` on the old main `ZooKeeperProvider` constructor and on
  `ZooKeeperService(string)`.
- `src/Rebalancer.Extensions.DependencyInjection`: `[Obsolete]` on the `AddRebalancerZooKeeper` overload without
  credentials; its non-deprecated path now builds providers with the ADR-0012 constructor (same behavior).
- Tests, example application and READMEs moved to non-deprecated APIs; version 2.2.0.

**Out of scope:**

- Any removal (3.0, ADR-0011 / ADR-0013), and ADR-0014 / ADR-0015 (3.0).
- Updating TM-0001 for ADR-0012's consequence (a caller-supplied service bypasses ADR-0011's credentials rule). That
  is a change to an accepted threat model and needs its own review (follow-up).

## Impact assessment

| Area                  | Impact                                                                                                                                                                |
|-----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Callers of the deprecated APIs get CS0618 warnings; builds with warnings as errors fail until they move to the replacements in the release notes. No behavior change. |
| Services / components | None at runtime. All public signatures from 2.1.0 still exist (binary-compatible).                                                                                    |
| Data / schema         | None.                                                                                                                                                                 |
| Security              | ADR-0012's constructor lets a caller-supplied service decide ACLs; documented in XML docs, README and release notes; TM-0001 update is a follow-up.                   |
| Operations            | None. Any upgrade order works, including node by node for Redis from 2.1.x.                                                                                           |

## Preconditions

- [x] Decision records in place: ADR-0011, ADR-0012, ADR-0013 (Accepted, `approved-by` set, on the shared trunk).
- [ ] Tests / dry-run completed on the release branch: unit tests and Docker integration tests (Redis, SQL Server,
  ZooKeeper single/multi-client/ACL).
- [ ] Rollback procedure validated / dry-run tested.
- [ ] Pre-change test suite passing cleanly.

## Implementation plan

1. Review branch `release/2.2.0`, then merge it into `main` and push. That push publishes 2.2.0 (`release.yml`);
   merging is a human action.
2. No consumer action is required at upgrade time. Consumers move off deprecated APIs before upgrading to 3.0.

## Validation plan

- The "Release Packages" workflow run for the merge commit succeeds, including "Push to GitHub Packages".
- A consumer project that uses a deprecated API builds with CS0618 warnings, not errors.

## Rollback plan

Redeploy 2.1.0. No data or behavior changed, so any order works. Code that already uses the new ADR-0012 constructor
must go back to the deprecated constructor with a service argument.

## Communications

Release notes `docs/release-notes/2.2.0.md`: deprecation table with replacements, and the 3.0 outlook.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome (2026-10-18):** `ac6587a` merged into `main` (fast-forward) and pushed by dradoaica. The "Release
  Packages" workflow ran on that push and succeeded at 18:25:46 UTC, including "Push to GitHub Packages"
  (https://github.com/dradoaica/Rebalancer/actions/runs/39154065063). Published package versions were not listed from
  the agent session (the GitHub Packages API requires authentication).
- **Test evidence (pre-merge, on the tree that was released):** unit 40/40; ZooKeeper single-client, multi-client and
  ACL 22/22 against a 3-node ensemble; Redis and SQL Server 2/2 (Docker); build without CS0618 warnings.
- **Deviations:** the rollback dry-run precondition was not performed. The test preconditions were not ticked in the
  approved record (immutable once approved); their evidence is recorded here and in the commit message of `ac6587a`.
- **Follow-ups:** TM-0001 update for ADR-0012 (caller-supplied service bypasses the credentials rule); 3.0 change
  record (CR-0003).

## Sources

- ADRs: `docs/adrs/0011-zookeeper-acls-by-default-in-next-major-version.md`,
  `docs/adrs/0012-zookeeper-provider-entry-point-for-a-custom-service.md`,
  `docs/adrs/0013-explicit-providers-only-remove-static-registry.md`
- User stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`,
  `docs/user-stories/US-007-coordinate-via-zookeeper-with-barrier-mode.md`,
  `docs/user-stories/US-012-register-rebalancer-with-dependency-injection.md`
- Threat model: `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md`
