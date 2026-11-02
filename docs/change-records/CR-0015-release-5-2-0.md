---
id: CR-0015
status: Draft
date: 2026-11-02
owner: "dradoaica"
approved-by: [ ]            # set by a human only
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 5.2.0, after approval"
depends-on: [ "ADR-0034", "ADR-0035", "ADR-0036", "ADR-0017", "ADR-0026", "TM-0003", "PRB-0009", "CR-0014" ]
---

# Change record: Rebalancer 5.2.0 – Redis grants one record at a time, CI gates, deprecations for 6.0

## Summary

Releases 5.2.0 of all eight packages with the three decisions accepted after 5.1.0. ADR-0036: the Redis coordinator
writes client records one at a time, so a group settles over slow links (PRB-0009). ADR-0035: the release workflow runs
the unit tests and a dependency scan before it publishes, and a new test workflow runs them, plus the integration
suites without ZooKeeper, on pull requests and pushes. ADR-0034's 5.x step: the eight public types no public signature
uses are deprecated (`[Obsolete]`, diagnostic `RBR0034`) to become internal in 6.0, and `ClientNotFoundException` is
public. No configuration or data format change. Release notes: `docs/release-notes/5.2.0.md`. 6.0.0 follows with its
own change record, as the maintainer decided (2026-11-02).

## Motivation / drivers

- PRB-0009: over a link whose round trip is a sizeable fraction of the heartbeat, a Redis group could keep rebalancing
  without settling.
- ADR-0035: every merge into `main` without a CI-skip marker publishes, and nothing ran the tests first.
- ADR-0034: hosts get one release of compiler warnings before 6.0 removes the types from the public API.

## Scope

**In scope:**

- **ADR-0036 (Tier 1, accepted):** `src/Rebalancer.Redis/Clients/ClientService.cs`: `ModifyClientsAsync` writes each
  record in its own compare-and-set, conditioned on that record and the lease, in the order given; the first failure
  stops the loop and leaves the records written before it. SQL Server and PostgreSQL unchanged.
  - **Found while confirming, and kept as a requirement:** safety needs the coordinator's own record written first.
    TLC on the changed model with grants reaching any subset violated `NoDoubleAccess` (`MCSafety.cfg`, 12 states): a
    coordinator that steps down after writing a follower's grant but not its own keeps its earlier grant in its own
    record (a step-down records only `Waiting`), runs it again as a follower, and overlaps with the follower. The code
    already adds the coordinator's own request first (`Coordinator.cs`); with that order modelled, every configuration
    holds. The order is now commented as required and pinned by
    `CoordinatorDemotionTests.AGrant_WritesTheCoordinatorsOwnRecordFirst`. ADR-0036's safety argument (property 3)
    did not name this condition; a dated note on ADR-0036 records it (Preconditions).
  - TLA+: `docs/tlaplus/LeaderGlobalBarrier`: `GrantedRecordsOn`; a failed grant writes any subset of the fenced
    records that contains the coordinator's own record; a failure with every check passed counts against
    `MaxFailures`. Results in `docs/tlaplus/README.md`.
  - Test: `RedisClientRecordTests.AGrantThatFailsOnOneRecord_KeepsTheRecordsWrittenBeforeIt` on Redis 7.0 and Valkey
    (failed before the change on both). ADR-0036 asks for a unit test; the unit-test project has no Redis fake or
    mocking library, and adding one is a dependency change, so it runs against the real servers.
- **ADR-0035 (Tier 1, accepted):** `.github/workflows/release.yml` runs `dotnet test tests/Rebalancer.UnitTests` and
  the dependency scan before `Pack`; new `.github/workflows/tests.yml` (pull requests and pushes to `main`): build,
  unit tests, scan, integration tests without ZooKeeper (`-- xUnit.MaxParallelThreads=3`). New
  `scripts/check-vulnerable-packages.sh`: fails on a High or Critical finding, direct or transitive, and lists the
  others; its filter was tested on synthetic reports (a High fails, Moderate and Low pass). Same actions as before
  (`actions/checkout@v6`, `actions/setup-dotnet@v5`); no new third-party action.
- **ADR-0034, 5.x step (Tier 1, accepted):** `[Obsolete(..., DiagnosticId = "RBR0034")]`, a warning naming 6.0, on
  `Rebalancer.LeaseCoordination.Roles.ClientEvent`, `.Roles.CoordinatorToken`, `.Roles.EventType`,
  `.Store.AssignmentStatus`, `Rebalancer.Redis.ResourceGroup`, `.Resources.Resource`, `.Utils.Constants`,
  `Rebalancer.SqlServer.Connections.ConnectionHelper`; the libraries and the tests suppress `RBR0034` only.
  `ClientNotFoundException` public, documented on `IClientService.KeepAliveAsync`. Checked first: no public
  signature uses the eight types (only internal types and the eight themselves). Contract test:
  `PublicApiDeprecationTests`.
- Versions 5.2.0 (`src/Directory.Build.props`); docs: release notes, README / CONTRIBUTING (CI), traceability matrix,
  PRB-0009, `docs/tlaplus/README.md`.

**Out of scope:**

- PRB-0009's full fix: ADR-0037 (Proposed), for 6.0.
- 6.0.0: the eight types internal (ADR-0034), with CR-0016 and a migration guide entry.

**Versioning.** A minor release: the deprecations and the public exception are additive (ADR-0034), and the Redis
change keeps the stored format and the protocol. The strongest argument for a patch: hosts see no new API they must
use. A minor marks the new public type and the new warnings.

## Impact assessment

| Area                  | Impact                                                                                                                                                                         |
|-----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Hosts that use one of the eight types get warning RBR0034 (suppressible). Custom `IClientService` implementations can throw `ClientNotFoundException`. Nothing else to change. |
| Services / components | Redis: a grant or stop takes one transaction per node instead of one for all; a failed grant may leave some records written (safe, see Scope).                                 |
| Data / schema         | None.                                                                                                                                                                          |
| Security              | TM-0003 unchanged: same keys, commands and ACL user. CI now blocks a publish with a High or Critical vulnerable dependency (ADR-0035).                                         |
| Operations            | Nodes can be upgraded one at a time: 5.1 and 5.2 Redis nodes read and write the same records, and a 5.1 coordinator's all-or-nothing grant is one of 5.2's behaviours.         |

## Preconditions

- [x] Decision records: ADR-0034, ADR-0035, ADR-0036 (Accepted by dradoaica; on `main`).
- [x] Build: 0 warnings (2026-11-02).
- [x] Unit tests: 121/121 (2026-11-02).
- [x] Redis suite (all but the network-fault test) on Redis 7.0 and Valkey: 55/55 with the change (2026-11-02).
- [x] TLC on `LeaderGlobalBarrier`, all eight configurations, on the final model: all hold (2026-11-02, TLC v1.8.0,
  downloaded for this session with dradoaica's authorization; counts in `docs/tlaplus/README.md`). The model before
  ADR-0036 still gives its recorded 12,956,398 states under `MCSafety.cfg`; the variant without the own-record-first
  order violates `NoDoubleAccess` (Scope).
- [x] `RedisNetworkFaultTests`, seed `1196549739`, alone on Redis 7.0 and Valkey (ADR-0036): **19 of 20** passed in
  10 runs, and 5 of 6 in 3 more runs with a trace: 2 failures in 26, both on Redis 7.0, both liveness, no overlap.
  Before the change: 3 of 6 failed. **ADR-0036's confirmation (10 runs without a failure) is not met**; see the
  deviation below.
- [ ] **Deviation, for the maintainer to accept:** ADR-0036 ships as a partial fix of PRB-0009. The trace over the
  3 runs (151 failed attempts, none reaching the 10th): most failed because that node's keep-alive rewrote the record
  meanwhile, and 7 with neither the record nor the lease changed. StackExchange.Redis implements the conditions
  with `WATCH`, which covers the whole key: any write to the group's clients hash or lease hash aborts the
  transaction. Keep-alives and lease renewals use the same compare-and-set and so compete with each other too; the
  second traced failure ran no client write out of attempts, so one of those is the likely cause (unverified). A
  full fix needs writes that do not depend on the whole hash staying unchanged: ADR-0037 (Proposed, for 6.0).
  The maintainer decided on 2026-11-02 to ship ADR-0036 as a partial fix ("Ship ADR-0036 as a partial fix").
- [ ] Integration tests, all but ZooKeeper: (pending).
- [ ] Dependency scan with the new script: (pending on the final branch).
- [ ] ZooKeeper integration tests: proposed **N/A**: no ZooKeeper code changed.
- [ ] Rollback rehearsal: proposed **N/A**: no stored data or protocol change; see Impact (Operations).
- [x] ADR-0036's missing condition (the coordinator's own record first): recorded as a dated note on ADR-0036, an
  exception to its immutability made at dradoaica's request (in session 2026-11-02). `govern_check.py --base main`
  reports it as GOV-10 on ADR-0036; that finding is this exception and expected.
- [ ] The new workflow on GitHub: runs on the pull request or the first push; ADR-0035 leaves the runner's memory for
  `MaxParallelThreads=3` to confirm there.

## Implementation plan

1. Code, tests, model, workflows, docs and version 5.2.0 on branch `agent/release-5.2.0`.
2. Complete the preconditions; record the results here.
3. Merge into `main` (human) and push (human). The push runs the gated release and publishes 5.2.0; the pushed head
   commit must not carry a CI-skip marker.

## Validation plan

- "Release Packages" succeeds, including the new "Unit Tests" and "Dependency Scan" steps, and pushes the eight
  packages at 5.2.0.
- "Tests" succeeds on the same push.

## Rollback plan

Redeploy 5.1.0; no data or configuration to undo. If the gated release fails, nothing is published; fix and push again.

## Communications

Release notes `docs/release-notes/5.2.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome:** {after the release}
- **Follow-ups:** {after the release}

## Sources

- ADRs: `docs/adrs/0034-public-api-of-the-lease-based-backends-in-6-0.md` (Decision Outcome: 5.x step),
  `docs/adrs/0035-tests-and-dependency-scan-in-ci.md` (Decision Outcome),
  `docs/adrs/0036-redis-coordinator-writes-client-records-one-at-a-time.md` (Decision Outcome, Confirmation),
  `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md`, `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`
- Threat model: `docs/threat-models/TM-0003-redis-coordination.md` (unchanged)
- Problem record: `docs/problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md`
- Previous release: `docs/change-records/CR-0014-release-5-1-0.md`
