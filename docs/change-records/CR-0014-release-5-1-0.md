---
id: CR-0014
status: Approved
date: 2026-11-02
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only (dradoaica, in session 2026-11-02)
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 5.1.0, after approval"
depends-on: [ "CR-0013", "ADR-0026", "ADR-0030", "ADR-0032", "ADR-0033", "TM-0002", "TM-0004", "US-0021", "US-0026", "PRB-0007", "PRB-0008" ]
---

# Change record: Rebalancer 5.1.0 – fixes from the 5.0 cross-check

## Summary

Releases 5.1.0 of all eight packages with the fixes found by the review after 5.0.0 (CR-0013, Post-implementation) and
the decisions on its open follow-ups. No public API, configuration or data format change: SQL Server and PostgreSQL
clients carry their fencing token and read the lease time after waiting for the lock; `RecreateClientAsync` works when
the record still exists; a PostgreSQL coordinator that stalls while assigning cannot block the others. ADR-0034 (the
6.0 public API) and ADR-0035 (tests in CI) are accepted but not part of this release. Release notes:
`docs/release-notes/5.1.0.md`. The decisions marked "decided" below were proposed by the agent and approved by
dradoaica with this record (2026-11-02).

## Motivation / drivers

- The cross-check of 5.0.0 found two defects that weaken safety checks (A1: PRB-0007's token check compared 0 with 0
  on SQL Server and PostgreSQL; A14: SQL Server judged a lease's age by the time before the lock wait) and several
  liveness and API defects (A2, A4, A5, A15).
- TM-0004 revision 2 (Accepted) relies on the PostgreSQL stall bound in `StartActivityAsync` (A2).
- Open follow-ups of CR-0013 needed a decision.

## Scope

**In scope (code, Tier 3; tests first, each shown failing before its fix where noted):**

- **A1:** SQL Server and PostgreSQL `KeepAliveAsync` return the record's `FencingToken` (Redis did already); the
  `*ClientRecordTests` assert it.
- **A2, A3:** PostgreSQL `StartActivityAsync` sets the stall bound (`lock_timeout`,
  `idle_in_transaction_session_timeout`) before it locks client rows, shared with the lease operations
  (`Connections/StallBound.cs`).
- **A4:** `GetAssignedResources(CancellationToken)` returns at once after an abort or stop (US-0021); failed before.
- **A5:** a coordinator relinquishes the lease with the token it was granted, also when its role never ran.
- **A14 (confirmed):** SQL Server's lease acquire and renew read `GETUTCDATE()` in a second statement, after the
  locking `UPDATE`: inside the `UPDATE` it is fixed when the statement starts, before the lock wait, so a node that
  waited saw the lease younger than it was and was denied a lease that had expired. Liveness only; the renewal stamp
  was already correct. `SqlServerLeaseClockTests` failed before. A missing time result now fails the request instead
  of reading a default time.
- **A15 (decided: replace, as on Redis):** SQL Server and PostgreSQL `CreateClientAsync` replace an existing record
  with a new registration (`Waiting`, `StopActivity`, no resources, token 1) instead of failing on the primary key, so
  `RecreateClientAsync` behaves on every backend as on Redis. The node role already has `UPDATE` on the client table.
  `CreatingAnExistingClient_ReplacesItsRecordWithANewRegistration` failed before on both.
- Smaller fixes: per-instance start lock (A17), the Redis renewal error message (A12), a parameter name (A16), dead code
  and three `InternalsVisibleTo` entries removed (A10, A11), comments naming PostgreSQL and ADR-0016 (A8, A9), XML
  contract docs on `ILeaseService`, `IClientService`, `IResourceService`.
- Tests: the cross-check's test findings B2, B4, B5, B8, B9, B11–B14, B16; the shadowing test uses decoy tables that can
  take effect; a PostgreSQL test that a renewal which waited for the row lock is stamped after the wait (failed with
  `now()`); a SQL Server test of the 900-byte resource key (PRB-0008).
- **Test runs (decided):** the integration tests run at most 3 at a time by default
  (`tests/Rebalancer.IntegrationTests/xunit.runner.json`; `-- xUnit.MaxParallelThreads=N` overrides it), and the SQL
  Server, PostgreSQL and Redis network-fault tests run alone after the others (`NetworkFaultCollection`).
- Build (Tier 4): one `<Version>` in `src/Directory.Build.props`, no BOMs in project files, `end_of_line = lf` in
  `.editorconfig`, a redundant `Nullable` setting removed from an example.
- Versions 5.1.0 (`src/Directory.Build.props`).

**In scope (docs and decisions, Tier 4):**

- **C3, support policy (decided: no 4.3.x line):** SECURITY.md supports 5.1.x only and says that .NET 8 and .NET 9
  reach end of support on 10 November 2026 (Microsoft's .NET blog, "dotnet-8-9-end-of-support", 2026-06-29; checked
  2026-11-02). A 4.3.x line would patch a library on runtimes that get no security fixes themselves eight days later.
- **C12:** SECURITY.md's PostgreSQL text follows TM-0004 revision 2 (D2: per-transaction timeouts).
- **B6 / PRB-0008 (decided: document, no schema change):** the README states that a resource's group and name together
  fit in 900 bytes on SQL Server; `SqlServerSchemaTests` shows 900 bytes accepted and 901 rejected (error 1946).
  Changing `ResourceName` would be a Tier 1 schema change for existing databases, for names nobody has reported.
- **B7 (decided: keep, record):** the PostgreSQL index `clients_resource_group` is the one structural difference
  between the scripts; SQL Server scans `RBR.Clients` (one row per node). Recorded in `db/postgresql/RebalancerTables.sql`.
  The other differences (column defaults, `NCHAR(500)` padding of `CoordinatorServer`) change no query result the
  backends use.
- **A13 (decided: keep, record):** the coordinator checks its lease with a separate `HoldsLeaseAsync` call per tick,
  not in the keep-alive as ADR-0026's outline (item 2, "to be confirmed in review") proposed. Same guard, one more
  round trip; every write is guarded anyway. Recorded here and at the call site.
- **ADR-0022 `postgresql` value (decided: no ADR change):** the value is authorized by US-0026 (criterion 4) and
  documented in the 5.0.0 release notes and TM-0004; ADR-0022 stays as accepted.
- **B15, HealthChecks references (decided: keep):** `Rebalancer.Extensions.Diagnostics.HealthChecks` references the
  health-check package and its `.Abstractions`; removing the redundant one is a dependency change (Tier 1) with no
  effect on what hosts restore.

**Out of scope:**

- ADR-0034 (deprecations for 6.0) and ADR-0035 (tests and scan in CI): accepted, to be released with their own change
  records.
- The Redis network-fault liveness failure (PRB-0009, see Preconditions).

**Versioning.** A minor release, as the maintainer asked (2026-11-02). The strongest argument for a patch release:
every change fixes documented behavior and adds no API. A minor marks the changed `RecreateClientAsync` behavior on
SQL Server and PostgreSQL (it no longer throws when the record exists) and the new support policy.

## Impact assessment

| Area                  | Impact                                                                                                                                                                                                   |
|-----------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | None to change. `RecreateClientAsync` on SQL Server and PostgreSQL succeeds where it threw. After an abort, `GetAssignedResources(token)` returns instead of waiting for the token. .NET 8/9 hosts: see C3. |
| Services / components | SQL Server lease calls run one more statement in the same batch; a node that waited for the group row may now take a lease that expired during the wait (as intended).                                    |
| Data / schema         | None. The scripts change only in comments.                                                                                                                                                               |
| Security              | TM-0004 revision 2 is met (A2). TM-0002 unchanged: the SQL Server change is a timing fix. No dependency change; scan clean.                                                                               |
| Operations            | Nodes can be upgraded one at a time: the stored records and the protocol are unchanged.                                                                                                                  |

## Preconditions

- [x] Decision records: none required (no Tier 1 change); the ADR-0026 deviation is recorded above.
- [x] Build: succeeded (2026-11-02).
- [x] Unit tests: 110/110 (2026-11-02).
- [x] Integration tests, all but ZooKeeper, on the final branch, with the new defaults (3 at a time, network-fault
  tests alone): **110/111** (2026-11-02, 27.7 min). The failure: `RedisNetworkFaultTests` on Valkey 8.1.11, liveness
  ("the nodes did not converge after the churn", seed `1196549739`), while running alone; the SQL Server, PostgreSQL
  and Redis 7.0 network-fault tests passed. Opened as PRB-0009. The run before A14/A15 was 106/107.
- [x] `RedisNetworkFaultTests` is flaky on `main` too: replayed alone, it failed twice on Redis 7.0 with the fixes, and
  on clean `main` (`e62e30c`) it failed once on Valkey and passed twice on Redis 7.0 (2026-11-01). Not caused by this
  change; it fails on liveness ("never converged"), never on safety.
- [x] Packages: `dotnet pack` of every `src/` project, as the release workflow runs it, gives the eight packages at
  5.1.0 from `src/Directory.Build.props`; their dependencies on each other are 5.1.0 (2026-11-02).
- [x] Dependency vulnerability scan (`dotnet list Rebalancer.slnx package --vulnerable --include-transitive`): no
  vulnerable package in any of the 13 projects (2026-11-02, nuget.org).
- [x] ZooKeeper integration tests: **N/A**, accepted by dradoaica with this record (in session 2026-11-02). Rationale: no ZooKeeper
  code changed; `Rebalancer.Core` changes only by removing three `InternalsVisibleTo` entries and a comment.
- [x] Rollback rehearsal: **N/A**, accepted by dradoaica with this record (in session 2026-11-02). Rationale: no stored data, schema or
  protocol change; 5.0.0 and 5.1.0 nodes read and write the same records. CR-0013 rehearsed 5.0.0 → 4.3.2.

## Implementation plan

1. Code, tests, docs and version 5.1.0 on branch `agent/review-fixes-code`.
2. Complete the preconditions above and record the results here.
3. Merge the branch into `main` (human) and push (human). The push publishes 5.1.0; the pushed head commit must not
   carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages", and publishes the eight packages at 5.1.0.

## Rollback plan

Redeploy 5.0.0; no data or configuration to undo.

## Communications

Release notes `docs/release-notes/5.1.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-11-02 (approved in session, before the release) |
| Release / CAB (or org) | dradoaica | 2026-11-02 (approved in session, before the release) |

## Post-implementation

- **Actual outcome:** (after the release)
- **Follow-ups:** (after the release)

## Sources

- Change record: `docs/change-records/CR-0013-release-5-0-0.md` (Post-implementation: the cross-check and follow-ups)
- ADRs: `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (Implementation outline, item 2),
  `docs/adrs/0030-postgresql-backend.md` (Clock A), `docs/adrs/0032-no-foreign-key-from-client-records-to-resource-groups.md`,
  `docs/adrs/0033-target-net-10-only.md`
- Threat models: `docs/threat-models/TM-0002-sql-server-coordination.md`,
  `docs/threat-models/TM-0004-postgresql-coordination.md` (revision 2, D2)
- User stories: `docs/user-stories/US-0021-report-aborted-state-on-sql-server-and-redis.md`,
  `docs/user-stories/US-0026-coordinate-via-postgresql-backend.md` (criterion 4)
- Problem records: `docs/problem-records/PRB-0007-assignment-delay-delays-keep-alives.md`,
  `docs/problem-records/PRB-0008-resource-key-can-exceed-sql-server-index-limit.md`
