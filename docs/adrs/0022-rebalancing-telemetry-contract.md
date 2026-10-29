---
id: ADR-0022
status: Accepted
date: 2026-10-19
decides: "how Rebalancer emits metrics and traces, and the names of its instruments, activities and tags"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0003", "US-0017", "US-0011", "TM-0001" ]
supersedes: [ ]
superseded-by: ""
---

# Emit metrics and traces through System.Diagnostics, with a fixed instrument contract

## Context and Problem Statement

US-0017 asks for metrics and traces about leadership, rebalances and assignments, so operators can watch a resource
group in their OpenTelemetry tooling. Today the library only logs (US-0011, `IRebalancerLogger`).

This record decides two things together, because the second is what makes the first expensive to reverse:
the API used to emit telemetry, and the **names** (meter, activity source, instruments, tags). Once dashboards and
alerts use those names, renaming one breaks them, so names are a public contract like the API.

**Finding (verified 2026-10-19, `src/Rebalancer.Core/obj/project.assets.json`):** `Rebalancer.Core` (netstandard2.1)
already resolves `System.Diagnostics.DiagnosticSource` 10.0.13, transitively through
`Microsoft.Extensions.Logging.Abstractions` 10.0.13. US-0017 assumed a new dependency; there is none, only a direct
reference to a package already in the graph.

## Decision Drivers

* No new package in any library's dependency graph; no OpenTelemetry SDK in the libraries (hosts choose exporters).
* One set of names across the three backends, stable across minor versions.
* Nothing sensitive in telemetry: no resource names (TM-0001 threat I1), connection strings or credentials.
* Low cardinality: tag values bounded by the number of groups and backends, not by resources or clients.
* No measurable cost when no listener is attached.

## Considered Options

* **A. `System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`** in `Rebalancer.Core`, with a direct
  reference to `System.Diagnostics.DiagnosticSource` at the version already resolved (10.0.13). OpenTelemetry .NET
  collects both with `AddMeter("Rebalancer")` / `AddSource("Rebalancer")`.
* **B. The `OpenTelemetry.Api` package** (unverified – please confirm the package name and netstandard2.1 support).
  A new dependency; OpenTelemetry .NET itself recommends the System.Diagnostics APIs for instrumentation libraries.
* **C. Log events only:** structured `IRebalancerLogger` messages from which hosts derive metrics. No new names, but no
  traces, and log text is a worse contract than instrument names.
* **D. No telemetry** (status quo).

## Decision Outcome

Proposed option: **A**, with the contract below.

The strongest argument against A is that it makes the names a public contract the maintainers must keep, and that a
host can already derive most of this from logs (C) without any library change. Given that, A is still preferred: the
APIs are already in the dependency graph, they cost nothing without a listener, and they are what OpenTelemetry
tooling consumes directly. C gives no traces and turns log wording into an implicit contract; B adds a package for no
gain; D leaves US-0017 unmet.

**Contract (names are final once this record is Accepted; changing one later needs a superseding ADR and a major
version):**

| Kind      | Name                               | Unit          | Tags                                                                                                                       | Meaning                                                  |
|-----------|------------------------------------|---------------|----------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------|
| Meter     | `Rebalancer`                       |               |                                                                                                                            | version = the `Rebalancer.Core` assembly version         |
| Counter   | `rebalancer.rebalances`            | `{rebalance}` | `rebalancer.backend`, `rebalancer.group`, `rebalancer.outcome` (`completed` / `aborted`)                                   | rebalances run by this node as coordinator               |
| Counter   | `rebalancer.coordinator.elections` | `{election}`  | `rebalancer.backend`, `rebalancer.group`                                                                                   | this node became coordinator                             |
| Counter   | `rebalancer.assignments`           | `{event}`     | `rebalancer.backend`, `rebalancer.group`                                                                                   | `OnAssignment` raised on this node                       |
| Counter   | `rebalancer.unassignments`         | `{event}`     | `rebalancer.backend`, `rebalancer.group`                                                                                   | `OnUnassignment` raised on this node                     |
| Histogram | `rebalancer.unassigned.duration`   | `s`           | `rebalancer.backend`, `rebalancer.group`                                                                                   | from an unassignment to the next assignment on this node |
| Source    | `Rebalancer`                       |               |                                                                                                                            | activity source, same version as the meter               |
| Activity  | `rebalancer.rebalance`             |               | `rebalancer.backend`, `rebalancer.group`, `rebalancer.fencing_token` (SQL Server, Redis) or `rebalancer.epoch` (ZooKeeper) | one per rebalance, on the coordinator                    |

* `rebalancer.fencing_token` and `rebalancer.epoch` identify the coordinator's tenure: both change when a new
  coordinator takes over, not on every rebalance (ZooKeeper's epoch is the `epoch` znode's version, incremented in
  `BecomeCoordinatorAsync`; verified 2026-10-19).
* `rebalancer.backend` is `sqlserver`, `redis` or `zookeeper` (`custom` for other providers). `rebalancer.group` is the resource-group name (bounded
  by the deployment, and not considered sensitive; resource names are never emitted).
* The names are exposed as public constants (`RebalancerTelemetry.MeterName`, `ActivitySourceName`) so hosts need not
  repeat strings. The instruments themselves stay internal to the libraries (backends get access through
  `InternalsVisibleTo`, as the tests do today).

### Consequences

* Good, because operators get counters, a duration histogram and a trace per rebalance with no new package anywhere.
* Good, because the names are explicit and versioned with the libraries.
* Bad, because `Rebalancer.Core` gains a direct package reference (same package and version as today) and the
  backends become instrumented code paths to keep correct.
* Bad, because instrument names cannot be refined freely after acceptance.
* Neutral: group names leave the process in telemetry; deployments that treat group names as sensitive must filter
  the tag in their collector.

### Confirmation

* Unit tests with `MeterListener` and `ActivityListener` (US-0017 criterion 5): each instrument fires with the tags
  above for SQL Server and Redis; no tag carries a resource name.
* The package graph of every library is unchanged apart from the direct reference (compare `project.assets.json`
  before and after).
* A benchmark or test is not required for "no cost without a listener"; the System.Diagnostics APIs guarantee it.

## Pros and Cons of the Options

### A. System.Diagnostics Meter and ActivitySource

* Good, because the package is already resolved; OpenTelemetry consumes it natively.
* Bad, because the names become a maintained contract.

### B. OpenTelemetry.Api

* Bad, because it adds a package to every host's graph for no capability A lacks.

### C. Log events only

* Good, because there is no new API surface.
* Bad, because there are no traces, and hosts must parse log text.

### D. No telemetry

* Good, because it costs nothing.
* Bad, because operators cannot see rebalancing behavior without reading logs.

## More Information

* Story: [US-0017](../user-stories/US-0017-emit-rebalancing-metrics-and-traces.md). Logging: [US-0011](../user-stories/US-0011-pluggable-rebalancer-logging.md).
* Protocol events: [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md),
  [ADR-0003](0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md).
* Sensitivity of resource names: [TM-0001](../threat-models/TM-0001-zookeeper-coordination-and-acl.md) (I1).
* External: OpenTelemetry semantic-convention style for names and units (lower-case, dot-separated, UCUM units).
* Release: a minor version (additive). Implementation follows only after this record is Accepted.

## Sources

- `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md` (ADR-0002: Coordinator, follower,
  lease, and resource store pattern for SQL Server and Redis backends)
- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md` (ADR-0003: ZooKeeper backend: leader
  election, barrier algorithms, and rebalancing modes)
- `docs/user-stories/US-0017-emit-rebalancing-metrics-and-traces.md` (US-0017: Emit rebalancing metrics and traces)
- `docs/user-stories/US-0011-pluggable-rebalancer-logging.md` (US-0011: Pluggable Rebalancer logging)
- `docs/threat-models/TM-0001-zookeeper-coordination-and-acl.md` (TM-0001: ZooKeeper coordination backend and znode
  ACLs)
