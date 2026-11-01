---
id: ADR-0035
status: Proposed
date: 2026-11-01
decides: "which checks run in CI before and besides publishing the packages"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0025", "ADR-0033" ]
supersedes: [ ]
superseded-by: ""
---

# Tests and a dependency scan in CI

## Context and Problem Statement

`.github/workflows/release.yml` restores, packs and pushes every package on each push to `main` and on each tag. It runs
no test and no dependency scan. Change records since CR-0009 run the scan by hand, CR-0007 and CR-0008 list "a CI
test job" as not planned, and PRB-0004 (closure) asks for a scan that fails on High or Critical. Every merge into
`main` without a CI-skip marker publishes (CR-0013, Post-implementation), so a build that fails its tests can be
published. The unit tests take about 40 s and need no Docker; the SQL Server, PostgreSQL and Redis integration suites
take about 10–15 minutes with Docker; the ZooKeeper suites need an ensemble and run for hours.

This record decides which checks run in CI.

## Considered Options

* **A. Gate publishing:** the release job runs the unit tests and the dependency scan before `Pack`, and stops on a
  failure.
* **B. A + a test workflow** on pull requests and pushes: build, unit tests, dependency scan, and the integration
  suites without ZooKeeper (Docker on the hosted runner).
* **C. Keep the checks manual**, recorded in each change record.

## Decision Outcome

Chosen option: "B. A + a test workflow", because A alone still lets a pull request merge untested, and the
integration suites are where the coordination defects of this repository were found (PRB-0001, PRB-0005, CR-0011).
The ZooKeeper suites stay manual: they need a three-node ensemble and run for hours.

* Release job: `dotnet test tests/Rebalancer.UnitTests` and
  `dotnet list Rebalancer.slnx package --vulnerable --include-transitive` before `Pack`; a vulnerable package of
  severity High or Critical, or a failed test, stops the job.
* Test workflow (`.github/workflows/tests.yml`): on pull requests and pushes, the same plus the integration suites
  without ZooKeeper:
  `dotnet test tests/Rebalancer.IntegrationTests --filter "FullyQualifiedName!~ZooKeeper" -- xUnit.MaxParallelThreads=3`.
  Whether the hosted runner's memory allows a higher parallelism is unverified – please confirm on the first runs.
* Change records keep recording the ZooKeeper runs, and the scan result for the released commit.

### Consequences

* Good, because nothing is published without passing unit tests and the scan, and pull requests show integration
  results before review.
* Bad, because every pull request and push takes 10–15 minutes of runner time.
* Bad, because a load-sensitive liveness test (the Redis network-fault test, US-0025 notes) can fail a run that a
  rerun passes; it should run on its own first.

## Sources

- `docs/adrs/0025-pin-patched-system-security-cryptography-pkcs.md` (why the scan matters)
- `docs/adrs/0033-target-net-10-only.md` (the runner's SDK)
