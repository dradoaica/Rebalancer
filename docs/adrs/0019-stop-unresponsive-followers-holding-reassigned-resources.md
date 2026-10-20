---
id: ADR-0019
status: Accepted
date: 2026-10-18
decides: "how a SQL Server or Redis follower is stopped when its resources were reassigned while it was unresponsive"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0002", "ADR-0014", "PRB-0003", "US-0005", "US-0006", "US-0013" ]
supersedes: [ ]
superseded-by: [ ]
---

# Stop unresponsive followers that still hold reassigned resources

## Context and Problem Statement

PRB-0003 (reproduced 2026-10-18): a SQL Server or Redis follower that pauses longer than the keep-alive expiry is
declared dead, and the coordinator reassigns its resources. The coordinator sends `StopActivity` only to **live**
clients, so the paused follower's record still says `ResourcesGranted` / `Active`. When the follower resumes, it keeps
processing until the coordinator sees it live again, rebalances, and the follower handles that stop. Two nodes process
the same resource during that window.

A paused process resumes all its threads at once, including the host's resource handlers. No library-side change can
stop a resumed node before it processes again; the best achievable is to stop it at its **first** follower tick after
resuming. Only resource-side fencing (the downstream system rejecting a stale writer) closes the window completely.

## Decision Drivers

* Shortest achievable window, using the existing coordinator → follower protocol.
* No new public API and no reliance on node-local clocks.
* Keep the roles identical across the two backends (ADR-0002).

## Considered Options

* **A. The coordinator also stops non-live clients that hold an assignment.** During a rebalance, the coordinator
  writes `StopActivity` (and clears the assigned resources) to every client of its group whose record is
  `ResourcesGranted` but whose keep-alive has expired. It uses the same fencing-token-guarded `StopActivityAsync` as
  for live clients. A resumed follower then reads `StopActivity` at its first tick and stops, through the existing
  follower path.
* **B. Follower self-fencing.** On keep-alive, the follower compares its previous keep-alive with the store clock. If
  it is older than the expiry, it stops and resets its own record. Same window as A, but it needs the previous
  keep-alive from the services (a service contract change), and followers would write the coordinator-owned status.
* **C. Expose the fencing token to resource handlers** (e.g. on `OnAssignmentArgs`), so downstream systems can reject
  writes from a stale assignment. Closes the window fully, but only for consumers whose resource system can check a
  token; additive public API.
* **D. Document only:** keep the keep-alive expiry well above the longest expected pause.

## Decision Outcome

Proposed option: **A**, with C left for a separate decision.

The strongest argument against A is that it does not close the window: a resumed node still processes until its first
follower tick, which is up to one follower interval (the heartbeat period divided by 2.5). It also writes to records of
nodes that may never come back. Those records then expire under ADR-0014.

A cuts the window from "until the coordinator notices the node and rebalances, then the follower's next tick" to
"the follower's first tick". It uses only the existing protocol, needs no new API, and keeps both roles identical.
B reaches the same window with more moving parts and a service contract change. D leaves the defect in place. C is
worth deciding on its own, because it is the only full fix and it needs consumer cooperation.

### Consequences

* Good, because a reassigned resource is released by a resumed follower at its first tick, through the same path a live
  follower uses.
* Good, because no public API or data format changes; behavior changes only for records of non-live clients.
* Bad, because the window is shortened, not closed; release notes and PRB-0003 must say so, and the operational advice (keep-alive expiry well above the longest pause) stays.
* Bad, because `StopActivityAsync` on SQL Server reports a fencing-token violation when fewer rows than requested are
  updated. A non-live record deleted between the read and the stop (e.g. by ADR-0014 expiry, which the same
  coordinator runs earlier in the same heartbeat) must not be counted as a violation. The implementation must keep the
  two from overlapping, and a test must cover it.

### Confirmation

* Unit tests (both backends): the PRB-0003 scenario; after a rebalance, the non-live follower's record is
  `StopActivity`, and when it resumes it invokes its stop actions once and does not start again with the old resources.
* Unit test: a non-live client without an assignment (`StopActivity` already) is not written again.
* The existing coordinator-demotion, expiry and assignment-safety tests stay green.

## More Information

* Corrective action of [PRB-0003](../problem-records/PRB-0003-resumed-follower-keeps-reassigned-resources.md).
  Builds on [ADR-0002](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) and
  [ADR-0014](0014-expire-stale-client-records.md).
* Release: next minor (3.1.0). Behavior change for non-live records only; no breaking API change.
