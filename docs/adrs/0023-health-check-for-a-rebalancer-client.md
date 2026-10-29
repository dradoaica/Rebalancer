---
id: ADR-0023
status: Accepted
date: 2026-10-19
decides: "how a host reports a RebalancerClient's health through Microsoft.Extensions.Diagnostics.HealthChecks"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0005", "ADR-0007", "ADR-0016", "US-0018", "US-0003", "US-0012" ]
supersedes: [ ]
superseded-by: ""
---

# Health check for a Rebalancer client in its own optional package

## Context and Problem Statement

US-0018 asks for a health check that reports when a node has stopped taking part in its resource group, so an
orchestrator can restart it or alert on it. This record decides where the check lives, which dependency it brings,
and how it finds the client it checks.

**Findings (verified in code, 2026-10-19):**

1. **There is no single DI-registered client.** Every `AddRebalancer*` extension registers `RebalancerClient` as **transient** (`src/Rebalancer.Extensions.DependencyInjection/*.cs`), so each resolve creates a new client with a
   new provider (ADR-0016). A check that resolves "the" client from DI would check a fresh, never-started client.
   US-0018 criterion 1 must change accordingly.
2. **`GetCurrentState()` does not reveal an abort on SQL Server and Redis.** Only `ZooKeeperProvider` returns
   `ClientState.Aborted`; the SQL Server and Redis providers keep reporting their last state after a non-recoverable
   error. A check must therefore also observe `RebalancerClient.OnAborted`.
3. **The client does not expose the group's lease expiry**, so US-0018's default "2 × the group's lease expiry" for
   `Degraded` cannot be computed by the check; the threshold has to be configured.

## Decision Drivers

* No new dependency for hosts that do not use health checks.
* The check observes the host's own, started client instance.
* Never blocks and never calls the backend (state only, as ADR-0007 does for `GetAssignedResources`).
* Correct for all three backends, including an abort on SQL Server and Redis.

## Considered Options

* **A. New optional package `Rebalancer.Extensions.Diagnostics.HealthChecks`** with
  `RebalancerClientHealthCheck : IHealthCheck`, constructed from the host's client instance (it subscribes to
  `OnAborted`), plus a `IHealthChecksBuilder.AddRebalancerClient(name, client, pendingThreshold)` extension. It
  depends on `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` (`IHealthCheck`) and
  `Microsoft.Extensions.Diagnostics.HealthChecks` (`IHealthChecksBuilder`), both at 10.0.x like the existing
  `Microsoft.Extensions.*` references (unverified – please confirm both package names, their netstandard2.1 support,
  and that `IHealthChecksBuilder` lives in the second one).
* **B. The same check inside `Rebalancer.Extensions.DependencyInjection`.** Every DI user takes the health-check
  packages, whether they use health checks or not.
* **C. Register `RebalancerClient` as a singleton** so a check can resolve it. A behavior change of the DI contract (ADR-0005, US-0012) for every host that resolves several clients; Tier 1 on its own.
* **D. Documentation only:** a sample check of about 20 lines in the README, which the host copies. No package, no
  dependency, no maintained API.

## Decision Outcome

Proposed option: **A**.

The strongest argument against A is D: the check is small, a host can write it in minutes from a documented sample,
and A adds a sixth package with two dependencies that the maintainers must version and release. If only one or two
hosts need the check, D is the better trade. A is still proposed because US-0018 asks for a reusable check, and the
two non-obvious traps above (transient registration, no `Aborted` state on SQL Server and Redis) are exactly what a
copied sample gets wrong. A also keeps the dependency out of every other package, which B does not, and avoids
changing the DI contract, which C does.

Behavior:

| Condition                                                           | Result      |
|---------------------------------------------------------------------|-------------|
| `OnAborted` was raised, or state `Aborted`                          | `Unhealthy` |
| State `Stopped`, `NotStarted` or `NoProvider`                       | `Unhealthy` |
| State `PendingAssignment` for longer than the configured threshold  | `Degraded`  |
| Otherwise (`Assigned`, or `PendingAssignment` within the threshold) | `Healthy`   |

The threshold is a constructor / registration parameter (default: 2 minutes, unverified – please confirm a sensible
default against typical `LeaseExpirySeconds`). The result's description carries the state only, never resource names
or connection details.

### Consequences

* Good, because hosts that want the check get one that is correct on every backend, with one line of registration.
* Good, because no existing package changes its dependencies or behavior.
* Bad, because a sixth package must be versioned, released and listed in the change records.
* Bad, because the host must pass its own client instance; the check cannot be fully "automatic" with transient
  registration.
* Neutral: `Stopped` is reported `Unhealthy` also during a deliberate shutdown; hosts normally stop health reporting
  first.

### Confirmation

* Unit tests per state mapping in the table, including an `OnAborted` raised by a SQL Server or Redis provider while
  `GetCurrentState()` still says `Assigned`.
* A test that the check makes no backend call (a provider whose service calls fail does not change the result).
* `dotnet list package --include-transitive` for the other five packages is unchanged.

## Pros and Cons of the Options

### A. Optional package

* Good, because the dependency stays opt-in and the traps are handled once.
* Bad, because of one more package to maintain.

### B. Inside the DI extensions package

* Good, because there is no new package.
* Bad, because every DI user takes the health-check dependencies.

### C. Singleton client registration

* Good, because a check could resolve the client itself.
* Bad, because it changes the DI contract for every host; needs its own decision and a major version.

### D. Documentation only

* Good, because it costs nothing to maintain.
* Bad, because each host re-implements it, and the abort trap is easy to miss.

## More Information

* Story: [US-0018](../user-stories/US-0018-report-client-health-to-the-host.md) (criteria 1 and 2 need the changes in
  the findings above).
* DI contract: [ADR-0005](0005-dependency-injection-for-provider-registration-v2.md),
  [US-0012](../user-stories/US-0012-register-rebalancer-with-dependency-injection.md); provider ownership:
  [ADR-0016](0016-rebalancer-client-owns-its-provider.md); non-blocking state:
  [ADR-0007](0007-non-blocking-get-assigned-resources.md), [US-0003](../user-stories/US-0003-graceful-shutdown-and-read-assignment-state.md).
* Out of scope here: making SQL Server and Redis report `ClientState.Aborted`; that would be a behavior change worth its
  own story.
* Release: a minor version (new package, additive). Implementation follows only after this record is Accepted.

## Note (2026-10-19, after 4.1.0)

Finding 2 was incomplete: SQL Server and Redis raise `OnAborted` also for errors they recover from (auto-recovery on),
not only for final ones. The 4.1.0 check therefore stayed `Unhealthy` after a recovered error. Fixed under US-0021:
the check clears the abort when the client is assigned again, so the table's first row reads "after `OnAborted`,
until the client is assigned again"; SQL Server and Redis now also report `ClientState.Aborted` after a final error.
The decision (option A) is unchanged. Appended at dradoaica's request (follow-up of the open-topics review).

## Sources

- `docs/adrs/0005-dependency-injection-for-provider-registration-v2.md` (ADR-0005: Dependency injection for provider
  registration (v2))
- `docs/adrs/0007-non-blocking-get-assigned-resources.md` (ADR-0007: Make `GetAssignedResources()` non-blocking in the
  SQL Server and Redis providers)
- `docs/adrs/0016-rebalancer-client-owns-its-provider.md` (ADR-0016: `RebalancerClient` owns and disposes its provider)
- `docs/user-stories/US-0018-report-client-health-to-the-host.md` (US-0018: Report a Rebalancer client's health to the
  host)
- `docs/user-stories/US-0003-graceful-shutdown-and-read-assignment-state.md` (US-0003: Graceful shutdown and read
  assignment state)
- `docs/user-stories/US-0012-register-rebalancer-with-dependency-injection.md` (US-0012: Register Rebalancer with
  dependency injection)
