---
id: CR-0008
status: Submitted
date: 2026-10-19
owner: "dradoaica"
approved-by: [ ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.2.0"
depends-on: [ "US-0021", "ADR-0023", "ADR-0020", "ADR-0022", "TM-0001", "TM-0002", "TM-0003", "CR-0007" ]
---

# Change record: Rebalancer 4.2.0 – Aborted state, health-check and Redis robustness fixes

## Summary

Releases 4.2.0 of all six packages. SQL Server and Redis clients report `ClientState.Aborted` after a final error
(US-0021); the health check recovers after a recovered error (4.1.0 defect); malformed Redis resource entries are
skipped (ADR-0020 move and reads). Release notes: `docs/release-notes/4.2.0.md`.

## Motivation / drivers

- 4.1.0 defect: the health check stayed `Unhealthy` after an error the client recovered from (ADR-0023 note).
- US-0021: `GetCurrentState()` and `BlockAsync` could not see an aborted SQL Server or Redis client.
- Rollback rehearsal (CR-0006): one malformed Redis resource entry crashed `StartAsync`.

## Scope

**In scope:**

- `src/Rebalancer.SqlServer/SqlServerProvider.cs`, `src/Rebalancer.Redis/RedisProvider.cs` (`aborted` state);
  `src/Rebalancer.Extensions.Diagnostics.HealthChecks/RebalancerClientHealthCheck.cs`;
  `src/Rebalancer.Redis/Utils/RedisHash.cs` (`TryDecode`), `Utils/PerGroupKeyMigration.cs`,
  `Resources/ResourceService.cs`.
- Tests: `AbortedStateTests`, `RebalancerClientHealthCheckTests` (unit); `RedisAbortedStateTests`,
  `RedisPerGroupKeysTests` (malformed entries), `ZooKeeperTelemetryTests` (integration).
- Versions 4.2.0; release notes; threat-model revisions (accepted); README and architecture overview.

**Out of scope:**

- Raising `OnAborted` only for final errors on SQL Server and Redis (public behavior change; needs its own record).

## Impact assessment

| Area                  | Impact                                                                                                    |
|-----------------------|-----------------------------------------------------------------------------------------------------------|
| Users / customers     | SQL Server / Redis hosts without auto-recovery see `Aborted` after a final error; `BlockAsync` returns.   |
| Services / components | None at runtime besides the state; malformed Redis entries are skipped instead of failing.               |
| Data / schema         | None.                                                                                                     |
| Security              | None (threat-model revisions for 4.1.0 telemetry accepted with this release).                             |
| Operations            | None. Any upgrade order.                                                                                  |

## Preconditions

- [x] Decision records and stories in place: US-0021 (readiness approved by dradoaica, 2026-10-19), ADR-0023 (note).
- [x] Tests on branch `agent/followups-2026-10-19`: see the evidence line below.
- [x] Rollback procedure validated (2026-10-19): 4.2.0 → released 4.1.0 (`a340daf`) on Redis and SQL Server, clean
  stop and killed client; 4.1.0 assigned `r1` after 6.3–6.4 s (clean) and 10.3–10.5 s (killed; lease expiry 5 s).
- [x] Pre-change test suite passing cleanly.

Evidence (2026-10-19): all six libraries build with warnings as errors; unit 59/59; SQL Server + Redis integration
24/24 (Docker), including the randomised runs; ZooKeeper suites 18/18 on a 3-node cluster (telemetry, single- and
multi-client; run earlier on this branch, no ZooKeeper code change since).

## Implementation plan

1. Merge branch `agent/followups-2026-10-19` into `main` (local merge by the agent at dradoaica's request); dradoaica
   pushes. The push publishes 4.2.0; the pushed head commit must not carry a CI-skip marker.

## Validation plan

- The "Release Packages" workflow run succeeds, including "Push to GitHub Packages".

## Rollback plan

Redeploy 4.1.0; no data or configuration to undo. The health-check defect and the old `Aborted` behavior return.

## Communications

Release notes `docs/release-notes/4.2.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      |      |      |
| Release / CAB (or org) |      |      |

## Post-implementation

- **Actual outcome:** {to be filled after the release}
- **Deviations:** {…}
- **Follow-ups:** {…}

## Sources

- User story: `docs/user-stories/US-0021-report-aborted-state-on-sql-server-and-redis.md`
- ADRs: `docs/adrs/0023-health-check-for-a-rebalancer-client.md` (note), `docs/adrs/0020-per-group-redis-leases-and-resources.md`
- Threat models: `docs/threat-models/TM-0001-…`, `TM-0002-…`, `TM-0003-…` (telemetry revisions)
- Previous release: `docs/change-records/CR-0007-release-4-1-0.md`
