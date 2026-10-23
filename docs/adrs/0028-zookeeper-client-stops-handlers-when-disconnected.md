---
id: ADR-0028
status: Accepted
date: 2026-10-23
decides: "when a ZooKeeper client stops its resource handlers after losing its connection to the ensemble"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0003", "ADR-0021", "ADR-0027" ]
supersedes: [ ]
superseded-by: [ ]
---

# A ZooKeeper client stops its handlers when it is disconnected

## Context and Problem Statement

A ZooKeeper client's barriers (resource-barrier mode) and client znode are ephemeral: ZooKeeper deletes them when it
expires the session, after about one session timeout without contact. Another client can then be assigned the
resources and start them. The disconnected client stops its handlers only when its own timer fires: the coordinator
and follower classes start it at `Disconnected` and give up after a third of the session timeout
(`ZooKeeperProvider` passes `sessionTimeout / 3`). The ZooKeeper Java client raises `Disconnected` two thirds of the
session timeout after the last contact (the .NET client is assumed to match; not verified). Both deadlines therefore
fall at about one session timeout after the last contact, and nothing guarantees the client stops first.

Model checking (`docs/tlaplus/LeaderResourceBarrier/MCLateExpiry.cfg`) confirms that when ZooKeeper expires the session
first, two clients run the same resource, even with ADR-0027. ADR-0003 does not mention this race. This record
decides when a disconnected client stops its handlers. It applies to both rebalancing modes; only resource-barrier
mode is modelled.

## Decision Drivers

* The handlers must stop before ZooKeeper can free the client's resources, with a margin that does not depend on races.
* Brief disconnections (an ensemble leader election takes seconds) should not cause a rebalance of the whole group.
* Add no new path: reuse the event loop and `ResourceManager` the client already has.

## Considered Options

* **A. A disconnect is a stop, a reconnect is a rebalancing event:** at `Disconnected`, invoke the stop actions and
  keep the session, the barriers and the assignment; at `SyncConnected` with the same session, respond to the current
  table again, as on a data watch; at `Expired`, continue as today.
* **B. Give up the session at `Disconnected`:** start a new session at once, which rebalances the group.
* **C. A shorter timer** after `Disconnected`.
* **D. Document only:** keep the session timeout well above the time handlers need to stop.

## Decision Outcome

Chosen option: **A**, because it stops the handlers about a third of the session timeout before ZooKeeper can expire
the session, without rebalancing the group on a blip, and the resume falls out of code that already exists.

The strongest argument against A is availability: every disconnection longer than two thirds of the session timeout
now stops all of the client's handlers, and hosts see stop and start callbacks they never saw before, so the change is
visible to them. D avoids that. But D relies on timing nobody controls, and the race exists on every network
partition, not only on pathological pauses.

### Implementation outline (to be confirmed in review)

1. **Disconnected:** the watcher queues a stop event; the event loop invokes the user stop actions and marks the
   `ResourceManager` suspended. It keeps its resources and barriers, and refuses to invoke start actions while
   suspended (an `OnStart` that finishes acquiring barriers during a disconnection waits for the reconnect).
2. **SyncConnected, same session:** the watcher queues `RebalancingTriggered`. A follower runs its normal response: it
   re-reads the table and re-acquires its barriers; `TryPutResourceBarrierAsync` already treats a barrier holding its
   own client id as acquired (`ZooKeeperService.cs`), so resuming the same resources needs no new code, and a table
   written during the disconnection is picked up. The coordinator restarts only its own share from the current table;
   a full rebalance would stop every follower for nothing.
3. **Expired**, or the disconnected timer: unchanged (new session); with ADR-0027 the resources are forgotten.

### Consequences

* Good, because the handlers stop at `Disconnected`, a third of the session timeout before ZooKeeper may expire the
  session (model: no violation with ZooKeeper allowed to expire the session first).
* Good, because a reconnect within the session timeout resumes without a group rebalance, through the existing
  response path.
* Bad, because hosts receive stop and start callbacks on disconnections; release notes must say so.
* Bad, because the stop actions must complete within about a third of the session timeout; the guide must say so.
* Neutral, because a process stalled longer than the session timeout cannot act at all; that risk stays as ADR-0021
  accepts it for SQL Server and Redis.

### Confirmation

* `docs/tlaplus/LeaderResourceBarrier/MCSuspendOnDisconnect.cfg` (`BarrierOwnerChecks = TRUE`,
  `SuspendOnDisconnect = TRUE`, ZooKeeper may expire first): no error, 7,230,389 distinct states; `MCLateExpiry.cfg`
  (ADR-0027 only) fails. `MCSuspendLiveness.cfg`: after disconnections the group converges again, 270,311 distinct
  states.
* An integration test that partitions a client from the ensemble (for example by pausing the ensemble's container) and
  asserts its handlers stop before the session timeout, and that a short partition resumes the same resources without
  a new table.

## Pros and Cons of the Options

### A. A disconnect is a stop, a reconnect is a rebalancing event

* Good, because it closes the race with margin, keeps the session, and reuses the response path.
* Bad, because of callbacks on blips and the stop-time budget.

### B. Give up the session at Disconnected

* Good, because it is simple: the existing session-loss path runs earlier.
* Bad, because every blip rebalances the whole group.

### C. A shorter timer

* Bad, because it narrows the race without closing it, and a timer shorter than the blips causes B's churn.

### D. Document only

* Good, because no behaviour change.
* Bad, because no session timeout setting makes the client stop first by construction.

## More Information

* Found with TLC on 2026-10-23; not reproduced against a real ensemble.
* Global-barrier mode is not modelled; its follower would re-read the `status` znode on reconnect instead of the table.
* Apache Curator's guidance for locks treats a suspended connection as a possibly lost lock; A follows that guidance.
  (External reference, not checked against this repository; please confirm.)
* Sources: `docs/tlaplus/README.md` (LeaderResourceBarrier findings), `docs/tlaplus/LeaderResourceBarrier/`,
  `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`, `src/Rebalancer.ZooKeeper/ResourceBarrier/Coordinator.cs`,
  `src/Rebalancer.ZooKeeper/ResourceBarrier/Follower.cs`, `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs`,
  `src/Rebalancer.ZooKeeper/ResourceManagement/ResourceManager.cs`, `src/Rebalancer.ZooKeeper/GlobalBarrier/`.
