---
id: PRB-0004
status: Root cause identified
date: 2026-10-19
opened: 2026-10-19
owner: ""
approved-by: [ ]            # closure review; set by a human only
severity: "High"
depends-on: [ "TM-0002", "CR-0008", "ADR-0025", "CR-0009" ]
---

# Problem record: Published SQL Server packages pull in a vulnerable System.Security.Cryptography.Pkcs

## Summary

`Rebalancer.SqlServer`, and through it `Rebalancer.Extensions.DependencyInjection`, resolve
`System.Security.Cryptography.Pkcs` 8.0.1 transitively through `Microsoft.Data.SqlClient` 7.1.1. That version is affected
by CVE-2026-84008 (GHSA-846f-f764-76mx, High, CVSS 7.5): unauthenticated quadratic CPU exhaustion through repeated
reparsing of attacker-controlled X.509 certificate data. Every published version that references
`Microsoft.Data.SqlClient` 7.1.1 is affected, including 4.2.0. The examples and test projects resolve 9.0.18, also
affected.

## Symptoms & impact

- **Symptoms:** none observed; found by `dotnet list package --vulnerable --include-transitive` and NuGet audit
  warnings (NU1902, NU1903) while building the examples (2026-10-19).
- **Impact:** a host that uses the SQL Server backend carries the vulnerable assembly. Whether it is reachable depends
  on whether attacker-controlled certificate data is parsed through `System.Security.Cryptography.Pkcs` in that host
  (SQL Server TLS certificates come from the server the host connects to). Not analysed further: please confirm the
  exposure for your deployments. Other backends (Redis, ZooKeeper) and `Rebalancer.Core` are not affected.

## Timeline

| When       | Event                                                                                             |
|------------|---------------------------------------------------------------------------------------------------|
| 2026-10-19 | Detected while verifying US-0022 (NuGet audit warnings in the examples; full scan of the solution) |
| 2026-10-19 | Problem record drafted; no mitigation yet                                                          |
| 2026-10-20 | Fix prepared: ADR-0025 (Pkcs 10.0.13 pinned), CR-0009 (release 4.2.1); scan clean on the branch    |

## Evidence

- `dotnet list Rebalancer.slnx package --vulnerable --include-transitive` (2026-10-19): `Rebalancer.SqlServer` and
  `Rebalancer.Extensions.DependencyInjection` → `System.Security.Cryptography.Pkcs` 8.0.1 (High); examples and tests →
  9.0.18 (High).
- `dotnet nuget why … System.Security.Cryptography.Pkcs`: `Microsoft.Data.SqlClient` (7.1.1) →
  `System.Security.Cryptography.Pkcs`.
- Advisory: https://github.com/advisories/GHSA-846f-f764-76mx. Affected `>= 8.0.0, <= 8.0.1`, `>= 9.0.0, <= 9.0.20`,
  `>= 10.0.0, <= 10.0.12`; patched 8.0.2, 9.0.21, 10.0.13.
- Related changes: `docs/change-records/CR-0008-release-4-2-0.md` (latest release, affected).

## Root cause analysis

The vulnerable assembly is a transitive dependency chosen by `Microsoft.Data.SqlClient`; nothing in the repository
pins a patched version. It was not caught because the builds treat compiler warnings as errors but NuGet audit
warnings do not fail the library builds (they appear for the examples and tests), and CI runs neither a build check
nor a vulnerability scan (open topic: CI test job).

## Corrective actions

| Action                                                                                                                 | Owner | Status |
|------------------------------------------------------------------------------------------------------------------------|-------|--------|
| Decide the fix in a decision record (Tier 1: dependency change): pin a patched `System.Security.Cryptography.Pkcs` as a direct reference of `Rebalancer.SqlServer`, or move to a `Microsoft.Data.SqlClient` that brings a patched one |       | Decided: ADR-0025 (Accepted) |
| Release a patch version of all packages with the fix, with a change record and release notes naming the CVE         |       | Approved: CR-0009 (4.2.1) |
| Pin or upgrade in the examples and test projects as well                                                              |       | Done on the branch: Tools pinned; others resolve 10.0.13 through `Rebalancer.SqlServer` |
| Remove the direct reference once `Microsoft.Data.SqlClient` depends on a patched Pkcs                                 |       | Open |

## Preventive measures

- **Agent Governance:** a vulnerability scan (`dotnet list package --vulnerable --include-transitive`) belongs in the
  pre-MR gate's static-analysis step; record it in the next change record's preconditions.
- **Tests / automation:** the CI test job (open topic) should run the scan and fail on High or Critical.
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** release notes of the fixing version tell SQL Server users to upgrade.
- **Decision records:** required for the fix (Tier 1).

## Known errors / workarounds

Until a fixed version is released, a host can add a direct reference to `System.Security.Cryptography.Pkcs` 8.0.2 or
later (9.0.21 / 10.0.13 for net9 / net10 hosts); NuGet then resolves the patched version for the whole application.

## Closure

{Open.}

## Sources

- Threat model: `docs/threat-models/TM-0002-sql-server-coordination.md` (dependencies of the SQL Server backend)
- Change record: `docs/change-records/CR-0008-release-4-2-0.md`
- Advisory: https://github.com/advisories/GHSA-846f-f764-76mx
