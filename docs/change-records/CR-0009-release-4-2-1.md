---
id: CR-0009
status: Implemented
date: 2026-10-20
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
risk: "Low"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 4.2.1 (security patch), as soon as approved"
depends-on: [ "ADR-0025", "PRB-0004", "TM-0002", "CR-0008" ]
---

# Change record: Rebalancer 4.2.1 – CVE-2026-84008 (System.Security.Cryptography.Pkcs)

## Summary

Security patch: `Rebalancer.SqlServer` references `System.Security.Cryptography.Pkcs` 10.0.13 directly (ADR-0025), so
hosts no longer resolve the vulnerable 8.0.1 / 9.0.18 that `Microsoft.Data.SqlClient` 7.1.1 brings (PRB-0004). All six
packages are released as 4.2.1. Release notes: `docs/release-notes/4.2.1.md`.

## Motivation / drivers

- CVE-2026-84008 (High) in a transitive dependency of every published SQL Server package, including 4.2.0.

## Scope

**In scope:** `src/Rebalancer.SqlServer/Rebalancer.SqlServer.csproj` and `examples/Rebalancer.RabbitMq.Tools/…csproj`
(direct reference); versions 4.2.1; release notes; PRB-0004 and ADR-0025.

**Out of scope:** a vulnerability scan in CI (open topic: CI test job); other dependency updates.

## Impact assessment

| Area                  | Impact                                                                   |
|-----------------------|--------------------------------------------------------------------------|
| Users / customers     | SQL Server users get the patched Pkcs on upgrade; no code change needed. |
| Services / components | None at runtime besides the patched assembly.                            |
| Data / schema         | None.                                                                    |
| Security              | Removes a known High vulnerability from the dependency graph.            |
| Operations            | None. Any upgrade order.                                                 |

## Preconditions

- [x] Decision record in place: ADR-0025 (Accepted by dradoaica, 2026-10-20; on the shared trunk with this release's merge).
- [x] Tests on branch `agent/security-pkcs-4.2.1`: see the evidence line below.
- [x] `dotnet list Rebalancer.slnx package --vulnerable --include-transitive`: no vulnerable package in any project (2026-10-20; before: High in six projects, including both SQL Server packages).
- [ ] Rollback procedure validated: redeploy 4.2.0 (reintroduces the vulnerability; no data change).
- [x] Pre-change test suite passing cleanly.

Approved by dradoaica on 2026-10-20 with one precondition open: the rollback to 4.2.0 is not rehearsed (no data
change; rolling back reintroduces the vulnerability).

Evidence (2026-10-20): all six libraries build with warnings as errors; unit 59/59; SQL Server integration 7/7 (Docker); the packed `Rebalancer.SqlServer.4.2.1.nupkg` declares `System.Security.Cryptography.Pkcs` >= 10.0.13. Redis
and ZooKeeper integration not run (no change to those backends). Rollback to 4.2.0 not rehearsed (no data change).

## Implementation plan

1. Merge `agent/security-pkcs-4.2.1` into `main` and push (human). The push publishes 4.2.1; the pushed head commit must
   not carry a CI-skip marker.

## Validation plan

- The "Release Packages" run succeeds, including "Push to GitHub Packages".
- The published `Rebalancer.SqlServer` 4.2.1 package lists `System.Security.Cryptography.Pkcs (>= 10.0.13)`.

## Rollback plan

Redeploy 4.2.0; no data or configuration to undo; the vulnerability returns.

## Communications

Release notes `docs/release-notes/4.2.1.md`, naming the CVE and the workaround for hosts that cannot upgrade.

## Approvals

| Role                   | Name      | Date       |
|------------------------|-----------|------------|
| Maintainer (code)      | dradoaica | 2026-10-20 |
| Release / CAB (or org) | dradoaica | 2026-10-20 |

## Post-implementation

- **Actual outcome (2026-10-20):** `agent/security-pkcs-4.2.1` merged into `main` as `732ecd7` ("Release 4.2.1"), merged
  locally by the agent at dradoaica's request and pushed by dradoaica. The "Release Packages" run on that push
  succeeded, including "Push to GitHub Packages" (21:22:43 UTC)
  (https://github.com/dradoaica/Rebalancer/actions/runs/39326897271). Published package versions were not listed from
  the agent session.
- **Deviations:** none.
- **Follow-ups:** close PRB-0004 (closure review by a human); remove the direct Pkcs reference once
  `Microsoft.Data.SqlClient` depends on a patched version; a vulnerability scan in CI (open topic: CI test job).
- **Rollback rehearsal (reported done by dradoaica, 2026-10-20):** rollback to 4.2.0 rehearsed; the rehearsal
  date and results were not recorded here.
- **Note (2026-10-20):** Scope names the tools project as `examples/Rebalancer.RabbitMq.Tools/…csproj`; the full path
  is `examples/Rebalancer.RabbitMq.Tools/Rebalancer.RabbitMq.Tools.csproj`.

## Sources

- ADR: `docs/adrs/0025-pin-patched-system-security-cryptography-pkcs.md`
- Problem record: `docs/problem-records/PRB-0004-vulnerable-transitive-pkcs-in-sql-server-packages.md`
- Previous release: `docs/change-records/CR-0008-release-4-2-0.md`
