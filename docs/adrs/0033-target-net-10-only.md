---
id: ADR-0033
status: Accepted
date: 2026-11-01
decides: "target framework of the published Rebalancer packages"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0025", "ADR-0029", "ADR-0030" ]
supersedes: [ ]
superseded-by: ""
---

# Target .NET 10 only

## Context and Problem Statement

All eight packages under `src/` target `netstandard2.1`; the tests and examples already target `net10.0`
(`global.json`: SDK 10.0.100). `netstandard2.1` keeps the packages usable on any .NET Core 3.0+ host, but it holds back
dependencies: Npgsql 9.x and 10.x have no `netstandard2.1` build, so the PostgreSQL backend is on 8.0.9, whose line
follows .NET 8's support (US-0026, implementation notes, item 5). It also keeps the code on the older API surface.
This record decides the target framework of the published packages.

## Considered Options

* **A. `net10.0` only.**
* **B. Multi-target `net8.0;net10.0`**: keeps .NET 8 hosts, at the cost of two builds per package.
* **C. Keep `netstandard2.1`.**

## Decision Outcome

Chosen option: "A. `net10.0` only", because 5.0.0 is a major release already, .NET 10 is the current LTS, and one
target lets every dependency move to its current line.

The strongest argument against A: it drops every host that is not on .NET 10. A host on .NET 8 or 9 cannot take
5.0.0 and stays on 4.3.x. B would keep .NET 8 hosts for one more major version at a modest cost. A is chosen on the
expectation that .NET 8 and .NET 9 leave support in November 2026 (dates unverified – please confirm), so few hosts
would stay on them; if that is wrong, B is the fallback.

* All projects under `src/` target `net10.0`. Package references that the shared framework provides on .NET 10 are
  removed where the SDK reports them as unnecessary; the others stay.
* The PostgreSQL backend moves to Npgsql 10.x (latest patch on nuget.org at implementation; 10.0.3 on 2026-11-01).
  US-0026 criterion 1 ("netstandard2.1, like the other backends") is replaced by this record.
* ADR-0025's pin of `System.Security.Cryptography.Pkcs` stays until `Microsoft.Data.SqlClient` brings a patched
  version on `net10.0`; the implementation checks which version resolves.
* Release: 5.0.0 (CR-0013); the release notes and the 5.0 migration guide say that 5.0 needs .NET 10.

### Consequences

* Good, because the PostgreSQL backend is on a supported Npgsql line, and dependencies are no longer capped by
  `netstandard2.1`.
* Good, because one target framework means one build and one set of test results per package.
* Bad, because hosts on .NET 8 or 9 (or any other `netstandard2.1` platform) cannot use 5.0; they stay on 4.3.x,
  which gets no fixes unless a 4.3.x patch release is made.
* Bad, because a later .NET major needs a decision whether to move the target again or add one.

## Sources

- `docs/adrs/0025-pin-patched-system-security-cryptography-pkcs.md` (the Pkcs pin)
- `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (5.0.0, the major release)
- `docs/adrs/0030-postgresql-backend.md` (Npgsql)
