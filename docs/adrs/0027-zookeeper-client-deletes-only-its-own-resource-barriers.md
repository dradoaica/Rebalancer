---
id: ADR-0027
status: Proposed
date: 2026-10-23
decides: "how a ZooKeeper client in resource-barrier mode removes resource barriers, and what it keeps of them across sessions"
owner: "dradoaica"
approved-by: [ ]            # set by a human only
decision-makers: [ ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0003", "US-0007", "US-0010" ]
supersedes: [ ]
superseded-by: [ ]
---

# A ZooKeeper client deletes only its own resource barriers

## Context and Problem Statement

In `RebalancingMode.ResourceBarrier` (ADR-0003) the barrier znode `{group}/resources/{r}/barrier` is the only thing that
keeps two clients from running resource `r` at once. A client creates it before starting `r` and deletes it after
stopping `r`. `ZooKeeperService.RemoveResourceBarrierAsync` deletes the znode **without checking who owns it**. Model
checking the implementation (`docs/tlaplus/LeaderResourceBarrier`, 2026-10-23) finds two paths that delete a barrier
another client holds, after which a third client can create it and start `r` while the owner still runs it:

1. **Cancellation, with no failure at all** (`MCAsBuilt.cfg`). A client waiting in `TryPutResourceBarrierAsync` for a
   barrier that the previous owner still holds is cancelled by a newer assignment table. The cancellation path in
   `ResourceManager.InvokeOnStartActionsAsync` deletes the barriers of **all** its new resources, including the one it
   was waiting for. TLC's trace needs three clients, two resources, and two changes (a client joins, a resource is
   removed) close enough that the coordinator writes three tables before the previous owner reacts.
2. **A lost session.** On an expired session `InvokeOnStopActionsAsync` stops the handlers, but each barrier delete
   throws, so `ResourceManager` keeps its resources. The provider keeps the `ResourceManager` for its whole life
   (`ZooKeeperProvider`), so the next `OnStop`, on the new session, deletes the barriers of those resources, which
   ZooKeeper freed when the old session ended and may have given to another client.

Neither is documented in ADR-0003, US-0007, or US-0010. This record decides how barriers are removed and what a client
keeps after losing a session.

## Decision Drivers

* The barrier must mean "this client may run the resource", whatever the order of events.
* One definition of a barrier's owner, used both when creating and when deleting it.
* No new dependency (ZooKeeperNetEx 3.4.12.4 is already used), no change to the znode layout.

## Considered Options

* **A. Compare the session:** delete a barrier only if its `Stat.getEphemeralOwner()` equals `getSessionId()`.
* **B. Track what this `OnStart` created:** cancellation deletes only barriers it created.
* **C. Compare the barrier's data with the client id:** delete a barrier only if its data is this client's id, with the
  version read alongside; this is the ownership test `TryPutResourceBarrierAsync` already applies.
* **D. Document only.**

Each of A–C is combined with clearing `ResourceManager`'s resources when `OnStop` cannot delete them.

## Decision Outcome

Chosen option: **C**, because creating and deleting a barrier then use the same, already existing definition of its
owner, and every delete goes through one method.

The strongest argument against C is that it costs one more round trip per barrier deleted (read, then delete), and B
fixes the two known paths without that cost. But B protects only the call sites known today; any future path that
deletes by name would reopen the hole. A is equally safe (the client id is unique per session, `c_` plus the sequence
number), but adds a second notion of ownership next to the one `TryPutResourceBarrierAsync` uses.

### Implementation outline (to be confirmed in review)

1. `GetResourceBarrierOwnerAsync` also returns the version from its `getDataAsync` result.
2. `RemoveResourceBarrierAsync` deletes only when the owner is this client id, passing that version; otherwise it does
   nothing. The stop, cancellation and error paths all call it, so all three are covered.
3. In `InvokeOnStopActionsAsync`, `SetResources(AssignmentStatus.NoAssignmentYet, …)` moves into a `finally`, so the
   local state changes even when deleting the barriers throws (an expired session).

Why read-then-delete is safe: once every client runs C, a barrier holding this client's id is deleted only by this
client or by ZooKeeper when the session ends, in which case the `delete` fails with `SessionExpired`. The version
check rejects a znode deleted and recreated in between.

### Consequences

* Good, because a barrier is removed only by its owner or by ZooKeeper on session end.
* Good, because `ResourceManager` no longer carries resources from an ended session into the next one, which
  ADR-0028 also relies on.
* Bad, because each delete costs one more round trip.
* Bad, because during a rolling upgrade, clients not yet upgraded still delete unchecked; the protection is complete only
  once every client in the group runs it (release notes must say so).

### Confirmation

* `docs/tlaplus/LeaderResourceBarrier/MCBarrierOwnerChecks.cfg` (`BarrierOwnerChecks = TRUE`, with disconnections and
  errors): no error, 83,241,009 distinct states; as built, `MCAsBuilt.cfg` fails without any failure injected. The
  model identifies the owner by session, which the client id identifies one to one.
* Unit tests with a fake `IZooKeeperService`: a cancelled `OnStart` leaves another client's barrier; an `OnStop` after
  a lost session deletes nothing and clears the resources.
* An integration test that reproduces path 1 against a real ensemble (slow `OnStop` on the previous owner, three
  tables in quick succession).

## Pros and Cons of the Options

### A. Compare the session

* Good, because `ephemeralOwner` is set by ZooKeeper itself.
* Neutral, because it is as safe as C.
* Bad, because it is a second notion of ownership next to the client id `TryPutResourceBarrierAsync` compares.

### B. Track what this OnStart created

* Good, because no extra round trip.
* Bad, because it fixes the two known call sites only.

### C. Compare the barrier's data with the client id

* Good, because one definition of ownership for create and delete, in an existing helper.
* Bad, because of the extra round trip.

### D. Document only

* Bad, because path 1 needs no failure; operators cannot avoid it.

## More Information

* Found with TLC on 2026-10-23; not reproduced against a real ensemble.
* Not decided here: what happens when ZooKeeper expires a session before the client notices (ADR-0028).
* Sources: `docs/tlaplus/README.md` (LeaderResourceBarrier findings), `docs/tlaplus/LeaderResourceBarrier/`,
  `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs`, `src/Rebalancer.ZooKeeper/ResourceManagement/ResourceManager.cs`,
  `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`.
