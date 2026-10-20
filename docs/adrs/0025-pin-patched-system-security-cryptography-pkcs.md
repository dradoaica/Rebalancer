---
id: ADR-0025
status: Accepted
date: 2026-10-20
decides: "which version of System.Security.Cryptography.Pkcs the SQL Server backend and the examples resolve"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "PRB-0004", "TM-0002", "US-0005" ]
supersedes: [ ]
superseded-by: [ ]
---

# Pin a patched System.Security.Cryptography.Pkcs (CVE-2026-84008)

## Context and Problem Statement

PRB-0004: `Rebalancer.SqlServer` (and `Rebalancer.Extensions.DependencyInjection` through it) resolves
`System.Security.Cryptography.Pkcs` 8.0.1 transitively through `Microsoft.Data.SqlClient` 7.1.1; hosts on .NET 9 or 10
resolve 9.0.18. Both are affected by CVE-2026-84008 (GHSA-846f-f764-76mx, High, CVSS 7.5: quadratic CPU exhaustion
from attacker-controlled X.509 data). Patched: 8.0.2, 9.0.21, 10.0.13.

**Facts (verified 2026-10-20, NuGet):** `Microsoft.Data.SqlClient` 7.1.1 is the latest stable version and asks for
Pkcs 8.0.1 (netstandard2.0, net462, net8.0) or 9.0.18 (net9.0); no newer SqlClient fixes it. Pkcs 10.0.13 targets
netstandard2.0, netstandard2.1, net462, net8.0, net9.0 and net10.0. NuGet resolves the highest version requested in a
graph.

## Decision Drivers

* Every host of the SQL Server backend resolves a patched version, whatever its target framework.
* Smallest change; removable once SqlClient asks for a patched version itself.

## Considered Options

* **A. Direct reference to Pkcs 10.0.13** in `Rebalancer.SqlServer` (and in `examples/Rebalancer.RabbitMq.Tools`, which
  references SqlClient directly).
* **B. Direct reference to Pkcs 8.0.2**, the smallest patched version for netstandard2.1.
* **C. Wait for a SqlClient release** that depends on a patched Pkcs; document the workaround.

## Decision Outcome

Chosen option: **A** (accepted by dradoaica, 2026-10-20).

The strongest argument against A is B: 8.0.2 is the minimal change for the library's own target (netstandard2.1)
and stays on the 8.x line SqlClient chose. But on a .NET 9 or 10 host SqlClient asks for 9.0.18, which is higher than
8.0.2, so NuGet would keep the vulnerable 9.0.18; only a reference at least as high as every line's patch fixes all
hosts, and 10.0.13 is the patched version of the highest line and supports every framework hosts use. C leaves
published packages vulnerable for an unknown time.

### Consequences

* Good, because hosts of 4.2.1 and later resolve Pkcs 10.0.13 on every framework.
* Bad, because the package now declares a dependency it does not use directly; remove it once SqlClient depends on a
  patched Pkcs (track in PRB-0004).
* Neutral: Pkcs 10.x on a .NET 8 host is a supported out-of-band package.

### Confirmation

* `dotnet list Rebalancer.slnx package --vulnerable --include-transitive` reports no vulnerable package.
* Unit and SQL Server integration tests pass.

## More Information

* Problem record: [PRB-0004](../problem-records/PRB-0004-vulnerable-transitive-pkcs-in-sql-server-packages.md).
* Advisory: https://github.com/advisories/GHSA-846f-f764-76mx
* Release: patch 4.2.1 with a change record.
