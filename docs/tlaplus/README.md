# TLA+ specifications

| Folder                                             | Protocol                                                                                      | Used by                                                                                                                                                                             |
|----------------------------------------------------|-----------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [`LeaderGlobalBarrier/`](LeaderGlobalBarrier/)     | Coordinator lease with fencing token, stop barrier for all clients, then grant                | SQL Server and Redis ([ADR-0002](../adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md)); ZooKeeper `RebalancingMode.GlobalBarrier` follows the same barrier |
| [`LeaderResourceBarrier/`](LeaderResourceBarrier/) | The coordinator writes a versioned assignment table; each client holds a barrier per resource | ZooKeeper `RebalancingMode.ResourceBarrier` ([ADR-0003](../adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md))                                                  |

Each folder has `RebalancerStore.tla` (the coordination store) and a protocol module named after the folder. The specs
document the protocols as implemented; they are not part of the build, and passing them is not a release gate.

The specs model the code as implemented, including ADR-0026, ADR-0027, ADR-0028 and the PRB-0005 fix. `LongPauses`
and `LateExpiryNotice` add environment conditions (stalls longer than the expiry; ZooKeeper expiring a session before
its process notices).

Running (TLC from `tla2tools.jar`, release page v1.8.0, build 2ef2af8; results below from 2026-10-23):

```
java -cp tla2tools.jar tlc2.TLC -workers auto -config MCSafety.cfg LeaderGlobalBarrier.tla
```

## LeaderGlobalBarrier

`RebalancerStore.tla` models the lease, the client records and the resources; `LeaderGlobalBarrier.tla` models the
nodes. Each action names the code it stands for:

| Spec                                                 | Code (`src/Rebalancer.SqlServer`, mirrored in `src/Rebalancer.Redis`)                                       |
|------------------------------------------------------|-------------------------------------------------------------------------------------------------------------|
| `AcquireLeadership`, `DenyLeadership`, `Demote`      | `StartLeadershipTask`, `ExecuteLeaseRenewals`, `Leases/LeaseService.cs`                                     |
| `CoordinatorTick`                                    | `Coordinator.ExecuteCoordinatorRoleAsync` up to the stop barrier, `StopUnresponsiveClientsAsync` (ADR-0019) |
| `CoordinatorGrant`, `CoordinatorResendStop`          | the barrier loop and `StartActivityAsync` in `Coordinator.RebalanceAsync`                                   |
| `CoordinatorExpireRecord`                            | `DeleteExpiredClientsAsync` (ADR-0014)                                                                      |
| `FollowerTick`                                       | `Follower.ExecuteFollowerRoleAsync`, `KeepAliveOrReRegisterAsync`                                           |
| `StoreError`                                         | a faulted task: `NotifyOfError`, record `Terminated`, `RelinquishLeaseAsync`, restart                       |
| `Pause` / `Resume`, `LeaseExpire`, `KeepAliveExpire` | a stall longer than the expiry (PRB-0001, PRB-0003); time is not modelled otherwise                         |

Abstractions:

- A coordinator heartbeat reads the clients and writes `StopActivity` in one step; the barrier check and
  `StartActivity` are one step. A rebalancing keeps the fencing token it started with, as `ClientEvent` does.
- `StopActivity` follows SQL Server (records with an older token are updated even when the call reports a violation);
  Redis writes all or none, which is one of the allowed behaviours.
- Each client id in `N` starts at most once: a restarted process registers a new id, and a cleanly stopped one is never
  reused (ADR-0014). `StoreError` keeps the id, as the implementation does.
- The allocation is any even split; the code's split is one of them. `OnAssignmentDelay` and the 3.x key migration
  are not modelled.
- ZooKeeper's global-barrier mode is not modelled separately. It maps as: lease and fencing token ↔ lowest sequential
  `clients/c_*` znode and the `epoch` version; per-client `StopActivity` / `ResourcesGranted` ↔ the `status` znode;
  `Waiting` ↔ a child of `stopped`; keep-alive expiry ↔ session expiry; fenced writes ↔ versioned `SetData`.
- [ADR-0026](../adrs/0026-coordinator-acts-only-under-a-valid-lease.md) and the
  [PRB-0005](../problem-records/PRB-0005-recovered-follower-never-reassigned.md) fix are modelled as implemented:
  `LeaseGuard` (the lease row names the coordinator and its token, no expiry clause) on the tick, the stop and the
  grant; the cached view keyed by the token; a restart resets the record; a client waiting under `StopActivity` is
  assigned again. A custom `IClientService` keeps the earlier behavior and is not modelled.

| Config                      | Checks                                                                        | Result                                                    |
|-----------------------------|-------------------------------------------------------------------------------|-----------------------------------------------------------|
| `MCSafety.cfg`              | `NoDoubleAccess`, `OneLeaseHolder` without long stalls                        | holds (12,956,398 distinct states)                        |
| `MCLiveness.cfg`            | `EventuallyConverged` without store errors                                    | holds (36,322 distinct states)                            |
| `MCRecoveredFollower.cfg`   | `EventuallyConverged` with one store error                                    | holds (15,727); violated before PRB-0005's fix, finding 1 |
| `MCLongPauses.cfg`          | `NoDoubleAccessOutsideResumeWindow` with long stalls (`OneLeaseHolder` holds) | holds (5,437,209); violated before ADR-0026, finding 2    |
| `MCLongPausesPermanent.cfg` | `EventuallyNoDoubleAccess` with two long stalls                               | holds (5,437,209); violated before ADR-0026, finding 3    |

A mutation check confirms that `NoDoubleAccess` has teeth: granting without waiting at the barrier violates it.

### Findings

1. **A follower that recovers from a store error is never assigned again.** `StoreError` marks its record `Terminated`;
   only a coordinator resets its own status to `Waiting` (`Coordinator.cs`, the `ClientStatus.Terminated` branch), and
   `GetActiveClientsAsync` skips `Terminated` records, so no coordinator includes the follower again. Its keep-alives
   keep the record from expiring (ADR-0014). It gets resources again only if it becomes the coordinator. ADR-0014's
   context ("a node that recovers ... moves it back to `Waiting`") holds for a coordinator only. Tracked in
   [PRB-0005](../problem-records/PRB-0005-recovered-follower-never-reassigned.md) (Tier 3); fixed, released in 4.3.0.
2. **A resumed coordinator runs reassigned resources for longer than its first tick.** ADR-0019 and PRB-0003 bound the
   window by the first tick after resuming; TLC refutes that for a coordinator: its tick does not check the lease, a
   fenced-out tick returns before stopping its handlers, and a coordinator that stalled at the barrier finishes it on
   its expired lease and starts its own handlers. On SQL Server the tick's `FencingToken >` check never fires, because
   `GetActiveClientsAsync` does not select `FencingToken` (Redis reads it). Fixed by ADR-0026, released in 4.3.0.
3. **After two long stalls, two clients can run the same resource forever.** The `Coordinator` object keeps its
   clients and resources across tenures; a node that regains the lease and sees the same lists skips rebalancing
   while still running a resource from its earlier tenure that was reassigned since. Fixed by ADR-0026, released in 4.3.0.

Findings 2 and 3 need a stall longer than the lease / keep-alive expiry, the condition ADR-0021 accepts. Each finding
was also confirmed in code; finding 1 was reproduced on a real SQL Server and Redis (PRB-0005). The findings describe
the
code before the fixes; the configs above check the code after them.

## LeaderResourceBarrier

`RebalancerStore.tla` models the znodes; `LeaderResourceBarrier.tla` models the processes. A process keeps its
`ResourceManager` across sessions, as the provider does.

| Spec                                               | Code (`src/Rebalancer.ZooKeeper`)                                                                                                                   |
|----------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|
| `NewSession`, `Elect`                              | `ZooKeeperProvider.RunStateMachine`: `EstablishSessionAsync`, `CreateClientNodeAsync`, `DetermineLeadershipAsync`, `BecomeCoordinatorAsync` (epoch) |
| `CoordinatorRebalance`, `CoordinatorDemote`        | `ResourceBarrier/Coordinator.cs`: `RebalanceAsync`, `IsRebalancingRequired`, the epoch watch                                                        |
| `Respond`, `FollowerPromote`                       | `ResourceBarrier/Follower.cs`: `ProcessStatusChangeAsync`, `PerformLeaderCheckAsync`                                                                |
| `AcquireBarrier`, `FinishStart`, `CleanUpBarriers` | `ResourceManager.InvokeOnStartActionsAsync` / `InvokeOnStopActionsAsync`, `TryPutResourceBarrierAsync`, `RemoveResourceBarrierAsync`                |
| `Disconnect`, `SessionLost`, `ServerExpire`        | the disconnected timer (a third of the session timeout), `Expired`, ephemeral znodes                                                                |
| `ErrorRestart`                                     | `PotentialInconsistentState`: `CleanUpAsync`, then auto-recovery with a new session                                                                 |

Abstractions:

- Responding to a table (cancel the rebalancing in progress, `OnStop`, read the table) is one step; each barrier is
  one step, in any order (the code follows the table's order).
- A cancelled `OnStart` deletes the barriers of all its new resources. In the code that happens when the cancellation
  finds it waiting for a barrier; otherwise it acquires the rest, starts, and stops at once, which deletes the same
  barriers.
- `LateExpiryNotice = TRUE` lets ZooKeeper expire a session before its process notices.
- Not modelled: `minimumRebalancingInterval`, `OnAssignmentDelay`, a stale cached epoch (`StaleEpoch`), exceptions
  from user handlers, and deleting a resource that still has a barrier (`deleteall`; a plain `delete` fails).
- [ADR-0027](../adrs/0027-zookeeper-client-deletes-only-its-own-resource-barriers.md) is modelled as implemented: a
  client deletes only the barriers it holds, and forgets its resources after a lost session.
- [ADR-0028](../adrs/0028-zookeeper-client-stops-handlers-when-disconnected.md) is modelled as implemented: the stop
  actions run at `Disconnected`, no handler starts while disconnected, and a reconnect makes the client respond to the
  current table again. The implementation restarts a coordinator's own share from its own resources rather than
  re-reading the table it wrote; the model re-reads it, which allows more behaviours.

| Config                          | Checks                                                                       | Result                                                 |
|---------------------------------|------------------------------------------------------------------------------|--------------------------------------------------------|
| `MCSafety.cfg`                  | `NoDoubleAccess`, `AccessHoldsBarrier`, no failures                          | holds (143,184); violated before ADR-0027, finding 4   |
| `MCLiveness.cfg`                | `EventuallyConverged`, no failures                                           | holds (8,543 distinct states)                          |
| `MCFailures.cfg`                | `NoDoubleAccess`, `AccessHoldsBarrier`, disconnections and errors            | holds (100,663,797 distinct states)                    |
| `MCLateExpiry.cfg`              | `NoDoubleAccess`, `AccessHoldsBarrier`, ZooKeeper may expire a session first | holds (7,230,389); violated before ADR-0028, finding 5 |
| `MCLivenessWithDisconnects.cfg` | `EventuallyConverged` after disconnections                                   | holds (270,311 distinct states)                        |

A mutation check confirms that `NoDoubleAccess` has teeth: acquiring a held barrier violates it.

### Findings

4. **A client deletes barriers it does not own.** `RemoveResourceBarrierAsync` deletes `resources/{r}/barrier` without
   checking its owner, and two paths reach it with someone else's barrier:
    - **Cancellation, with no failure.** A client waiting for a barrier still held by the previous owner is cancelled
      by a newer table; the cancellation path in `InvokeOnStartActionsAsync` deletes the barriers of all its new
      resources, including the one it was waiting for. TLC's trace (three clients, two resources): a client joins and a
      resource is removed in quick succession, so the coordinator writes three tables; a follower blocked on r2, which
      another client still runs, is cancelled, deletes that barrier, takes r2 under the next table and starts it while
      the other client is still running it.
    - **A lost session.** On an expired session, `InvokeOnStopActionsAsync` stops the handlers but every barrier delete
      throws, so `ResourceManager` keeps its resources. The provider keeps the `ResourceManager`, so the next `OnStop`
      on the new session deletes the barriers of those resources, which ZooKeeper had already freed and may have given
      to another client. Shown with a variant in which only the cancellation path checks the owner.

   Fixed by ADR-0027, released in 4.3.0. The cancellation path was reproduced on a real ensemble (`BarrierOwnershipTests`,
   which fail against the code before the fix).
5. **The barrier depends on the process noticing a lost session before ZooKeeper expires it.** The client gives up a
   third of the session timeout after `Disconnected`, which the ZooKeeper Java client raises two thirds of the session
   timeout after the last contact (the .NET client is assumed to match; not verified), so both happen about one
   session timeout after the last contact. If ZooKeeper expires the session first, its barriers go and another client
   can start the resource while the disconnected process still runs it. Fixed by ADR-0028, released in 4.3.0.

All findings come from model checking and reading the code. Finding 4 (cancellation path) was reproduced on a real
ensemble. Finding 5 was not reproduced as a double run; `DisconnectionTests` checks the fix on a real ensemble (handlers
stop while the ensemble is paused, and run again when the session survives). The findings describe the code before the
fixes; the configs above check the code after them.
