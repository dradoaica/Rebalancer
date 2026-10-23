------------------------- MODULE LeaderGlobalBarrier -------------------------
(* The coordinator / follower protocol of the SQL Server and Redis backends (ADR-0002):
   - a lease with a fencing token elects the coordinator (Leases/LeaseService.cs)
   - the coordinator stops all live clients, waits until all of them confirm (the global
     barrier), then grants each one its resources (Roles/Coordinator.cs)
   - followers read their record on every heartbeat and stop or start (Roles/Follower.cs)
   - every coordinator write is guarded by its fencing token (Clients/ClientService.cs)
   ZooKeeper's RebalancingMode.GlobalBarrier follows the same barrier; see README.md. *)
EXTENDS Integers, FiniteSets, TLC, RebalancerStore
CONSTANTS LongPauses,     \* TRUE: a node can stall longer than the lease / keep-alive expiry (PRB-0001, PRB-0003)
          MaxEnvEvents,   \* bound on node starts, stops, crashes and resource changes
          MaxFailures     \* bound on store errors and long pauses
VARIABLES role,         \* a function of client id to its role: "new" (not started yet), "off" (stopped or crashed),
                        \* "none" (no leadership result yet), "follower", "coordinator"
          myToken,      \* the fencing token of the node's last lease grant
          rb,           \* the coordinator's rebalancing: "idle" or "waiting" (for all clients to confirm stopped)
          rbRes,        \* the resources read when the current rebalancing started
          rbToken,      \* the fencing token the current rebalancing started with (ClientEvent.FencingToken)
          known,        \* the clients, resources and fencing token of the coordinator's last completed rebalancing
          stoppedFlag,  \* the coordinator's stoppedDueToInternalErrorFlag
          access,       \* the resources whose handlers a node runs (between OnStart and OnStop)
          paused,       \* the set of nodes whose process is stalled
          lagging,      \* the set of resumed nodes that have not run a heartbeat tick since (ADR-0019 window)
          envCtr,       \* number of environment events, bounded for model checking
          failCtr       \* number of failures, bounded for model checking

Role == { "new", "off", "none", "follower", "coordinator" }
NoKnown == [clients |-> {}, res |-> {}, token |-> 0]

vars_store == << lease, rec, resources >>
vars_node == << role, myToken, rb, rbRes, rbToken, known, stoppedFlag, access, paused, lagging >>
vars == << vars_store, vars_node, envCtr, failCtr >>

---------------------------------------------------------
(* INIT AND INVARIANTS *)

Init ==
  /\ StoreInit
  /\ role = [n \in N |-> "new"]
  /\ myToken = [n \in N |-> 0]
  /\ rb = [n \in N |-> "idle"]
  /\ rbRes = [n \in N |-> {}]
  /\ rbToken = [n \in N |-> 0]
  /\ known = [n \in N |-> NoKnown]
  /\ stoppedFlag = [n \in N |-> FALSE]
  /\ access = [n \in N |-> {}]
  /\ paused = {}
  /\ lagging = {}
  /\ envCtr = 0
  /\ failCtr = 0

TypeOK ==
  /\ StoreTypeOK
  /\ role \in [N -> Role]
  /\ myToken \in [N -> Nat]
  /\ rb \in [N -> { "idle", "waiting" }]
  /\ rbRes \in [N -> SUBSET R]
  /\ rbToken \in [N -> Nat]
  /\ known \in [N -> [clients : SUBSET N, res : SUBSET R, token : Nat]]
  /\ stoppedFlag \in [N -> BOOLEAN]
  /\ access \in [N -> SUBSET R]
  /\ paused \subseteq N
  /\ lagging \subseteq N

(* Two nodes never run the handlers of the same resource. A stalled node runs nothing until it
   resumes; on resuming, all its threads (including the handlers) run at once. *)
NoDoubleAccess ==
  \A r \in R : Cardinality({ n \in N \ paused : r \in access[n] }) <= 1

(* The window ADR-0019 / ADR-0021 (option D) accept: after a pause longer than the keep-alive
   expiry, a node may run reassigned resources until its first heartbeat tick (coordinator or
   follower); outside that window, never. Holds since ADR-0026 (MCLongPauses.cfg). *)
NoDoubleAccessOutsideResumeWindow ==
  \A r \in R : Cardinality({ n \in N \ (paused \cup lagging) : r \in access[n] }) <= 1

(* There is at most one coordinator holding a valid lease *)
OneLeaseHolder ==
  Cardinality({ n \in N : role[n] = "coordinator" /\ HoldsLease(n, myToken[n]) }) <= 1

Up == { n \in N : role[n] \notin { "new", "off" } }

(* Every running node is part of the last assignment, runs exactly what its record grants,
   and every resource is run by a node *)
Converged ==
  /\ \A n \in Up : /\ rec[n].present
                   /\ rec[n].status = "active"
                   /\ rec[n].cmd = "granted"
                   /\ access[n] = rec[n].res
  /\ Up # {} => \A r \in resources : \E n \in Up : r \in access[n]

---------------------------------------------------------
(* ALLOCATION (Coordinator.RebalanceAsync): an even split of the resources over the clients *)

Balanced(alloc, C, res) ==
  /\ \A m, o \in C : m = o \/ alloc[m] \cap alloc[o] = {}
  /\ UNION { alloc[m] : m \in C } = res
  /\ \A m, o \in C : Cardinality(alloc[m]) - Cardinality(alloc[o]) \in { -1, 0, 1 }

Allocation(C, res) ==
  CHOOSE alloc \in [C -> SUBSET res] : Balanced(alloc, C, res)

---------------------------------------------------------
(* LEADERSHIP TASK (SqlServerProvider/RedisProvider.StartLeadershipTask) *)

Running(n) ==
  /\ role[n] \notin { "new", "off" }
  /\ n \notin paused

(* TryAcquireLease granted: the node is now the coordinator with a new fencing token *)
AcquireLeadership(n) ==
  /\ Running(n)
  /\ role[n] \in { "none", "follower" }
  /\ LeaseFree
  /\ lease' = AcquiredLease(n)
  /\ role' = [role EXCEPT ![n] = "coordinator"]
  /\ myToken' = [myToken EXCEPT ![n] = lease.token + 1]
  \* the Coordinator object keeps its clients and resources across tenures (known is not reset)
  /\ UNCHANGED << rec, resources, rb, rbRes, rbToken, known, stoppedFlag, access, paused, lagging, envCtr, failCtr >>

(* TryAcquireLease denied: the node is a follower *)
DenyLeadership(n) ==
  /\ Running(n)
  /\ role[n] = "none"
  /\ ~LeaseFree
  /\ role' = [role EXCEPT ![n] = "follower"]
  /\ UNCHANGED << vars_store, myToken, rb, rbRes, rbToken, known, stoppedFlag, access, paused, lagging, envCtr, failCtr >>

(* TryRenewLease denied: the coordinator posts follower events from now on. A rebalancing in
   progress carries on (the role task is busy with it) and is fenced by the old token. *)
Demote(n) ==
  /\ Running(n)
  /\ role[n] = "coordinator"
  /\ ~HoldsLease(n, myToken[n])
  /\ role' = [role EXCEPT ![n] = "follower"]
  /\ UNCHANGED << vars_store, myToken, rb, rbRes, rbToken, known, stoppedFlag, access, paused, lagging, envCtr, failCtr >>

---------------------------------------------------------
(* COORDINATOR (Coordinator.ExecuteCoordinatorRoleAsync) *)

(* A fencing token violation ends the lease renewals; the leadership task then asks for the
   lease again *)
FencingViolation(n) ==
  role' = [role EXCEPT ![n] = "none"]

(* One coordinator heartbeat: keep-alive (or re-registration, ADR-0014), read the resources and
   live clients, and start a rebalancing when anything changed. Starting a rebalancing:
   - record StopActivity for all live clients (fenced)
   - record StopActivity for unresponsive clients that still hold resources (ADR-0019)
   - stop the local handlers and record Waiting for itself (PRB-0001) *)
CoordinatorTick(n) ==
  LET self == KeptAlive(n)
      reRegistered == ~rec[n].present
      flag == stoppedFlag[n] \/ reRegistered
      clients == LiveClients(n)
      t == myToken[n]
      unresponsive == UnresponsiveClients(n)
      mustRebalance == \/ self.status = "terminated"
                       \/ flag
                       \/ resources # known[n].res
                       \/ clients \cup {n} # known[n].clients
                       \* ADR-0026: the cache is keyed by the fencing token, so a new tenure rebalances
                       \/ known[n].token # t
                       \* PRB-0005: a client that is stopped and not granted needs an assignment
                       \/ \E m \in clients : rec[m].status = "waiting" /\ rec[m].cmd = "stop"
      stopRecs == StoppedRecords(clients, t)
      afterStop == [m \in N |-> IF m \in unresponsive /\ stopRecs[m].token <= t
                                  THEN [stopRecs[m] EXCEPT !.cmd = "stop", !.res = {}, !.token = t]
                                  ELSE stopRecs[m]]
  IN /\ Running(n)
     /\ role[n] = "coordinator"
     /\ rb[n] = "idle"
     /\ lagging' = lagging \ {n}
     \* ADR-0026: the tick first checks the lease; a fenced-out coordinator stops its handlers
     /\ IF (\E m \in clients : rec[m].token > t) \/ ~LeaseGuard(n, t)
          THEN /\ rec' = [rec EXCEPT ![n] = self]
               /\ access' = [access EXCEPT ![n] = {}]
               /\ FencingViolation(n)
               /\ stoppedFlag' = [stoppedFlag EXCEPT ![n] = flag]
               /\ UNCHANGED << rb, rbRes, rbToken >>
          ELSE IF mustRebalance
            THEN \* the live clients were read in this step, so StopActivity is fenced
                 /\ rec' = [afterStop EXCEPT ![n] = [self EXCEPT !.status = "waiting"]]
                 /\ access' = [access EXCEPT ![n] = {}]
                 /\ rb' = [rb EXCEPT ![n] = "waiting"]
                 /\ rbRes' = [rbRes EXCEPT ![n] = resources]
                 /\ rbToken' = [rbToken EXCEPT ![n] = t]
                 /\ stoppedFlag' = [stoppedFlag EXCEPT ![n] = FALSE]
                 /\ UNCHANGED role
            ELSE \* a re-registration always forces a rebalancing, so the handlers keep running
                 /\ rec' = [rec EXCEPT ![n] = self]
                 /\ UNCHANGED << role, rb, rbRes, rbToken, stoppedFlag, access >>
  /\ UNCHANGED << lease, resources, myToken, known, paused, envCtr, failCtr >>

(* The stop barrier: once every live client has confirmed (Waiting), grant the resources read
   at the start to the live clients and the coordinator in one fenced transaction, then mark
   itself Active and start its own handlers *)
CoordinatorGrant(n) ==
  LET clients == LiveClients(n)
      C == clients \cup {n}
      t == rbToken[n]
      alloc == Allocation(C, rbRes[n])
  IN /\ Running(n)
     /\ rb[n] = "waiting"
     /\ \A m \in clients : rec[m].status = "waiting"
     /\ rb' = [rb EXCEPT ![n] = "idle"]
     \* ADR-0026: the grant carries the lease guard
     /\ IF Fenced(C, t) /\ LeaseGuard(n, t)
          THEN /\ rec' = [GrantedRecords(alloc, t) EXCEPT ![n].status = "active"]
               /\ access' = [access EXCEPT ![n] = alloc[n]]
               /\ known' = [known EXCEPT ![n] = [clients |-> C, res |-> rbRes[n], token |-> t]]
               /\ UNCHANGED role
          ELSE /\ FencingViolation(n)
               /\ UNCHANGED << rec, access, known >>
  /\ UNCHANGED << lease, resources, myToken, rbRes, rbToken, stoppedFlag, paused, lagging, envCtr, failCtr >>

(* Every 12th check of the barrier, StopActivity is recorded again for clients not yet Waiting *)
CoordinatorResendStop(n) ==
  LET remaining == { m \in LiveClients(n) : rec[m].status # "waiting" }
      t == rbToken[n]
  IN /\ Running(n)
     /\ rb[n] = "waiting"
     /\ remaining # {}
     \* ADR-0026: StopActivity carries the lease guard (no row changes without the lease)
     /\ rec' = IF LeaseGuard(n, t) THEN StoppedRecords(remaining, t) ELSE rec
     /\ IF Fenced(remaining, t) /\ LeaseGuard(n, t)
          THEN UNCHANGED << role, rb >>
          ELSE /\ FencingViolation(n)
               /\ rb' = [rb EXCEPT ![n] = "idle"]
  /\ UNCHANGED << lease, resources, myToken, rbRes, rbToken, known, stoppedFlag, access, paused, lagging, envCtr, failCtr >>

(* ADR-0014: delete a record whose keep-alive expired long ago, only while holding the lease *)
CoordinatorExpireRecord(n) ==
  /\ Running(n)
  /\ role[n] = "coordinator"
  /\ rb[n] = "idle"
  /\ HoldsLease(n, myToken[n])
  /\ \E m \in N \ {n} : /\ rec[m].present
                        /\ ~rec[m].fresh
                        /\ rec' = [rec EXCEPT ![m] = NoRecord]
  /\ UNCHANGED << lease, resources, vars_node, envCtr, failCtr >>

---------------------------------------------------------
(* FOLLOWER (Follower.ExecuteFollowerRoleAsync) *)

(* One follower heartbeat: keep-alive (or re-registration, which stops local activity), then
   - StopActivity while Active: stop the handlers and record Waiting
   - ResourcesGranted while Waiting: record Active and start the handlers on the granted resources *)
FollowerTick(n) ==
  LET self == KeptAlive(n)
      reRegistered == ~rec[n].present
  IN /\ Running(n)
     /\ role[n] = "follower"
     /\ rb[n] = "idle"
     /\ lagging' = lagging \ {n}
     /\ CASE self.cmd = "stop" /\ self.status = "active" ->
               /\ rec' = [rec EXCEPT ![n] = [self EXCEPT !.status = "waiting"]]
               /\ access' = [access EXCEPT ![n] = {}]
          [] self.cmd = "granted" /\ self.status = "waiting" ->
               /\ rec' = [rec EXCEPT ![n] = [self EXCEPT !.status = "active"]]
               /\ access' = [access EXCEPT ![n] = self.res]
          [] OTHER ->
               /\ rec' = [rec EXCEPT ![n] = self]
               /\ access' = IF reRegistered THEN [access EXCEPT ![n] = {}] ELSE access
  /\ UNCHANGED << lease, resources, role, myToken, rb, rbRes, rbToken, known, stoppedFlag, paused, envCtr, failCtr >>

---------------------------------------------------------
(* TIME: expiry is allowed only when the owner cannot renew in time *)

(* The lease expires when its holder no longer renews it: it is off, it is not acting as the
   coordinator, or (LongPauses) it is stalled *)
LeaseExpire ==
  /\ lease.holder # Nil
  /\ ~lease.expired
  /\ \/ role[lease.holder] # "coordinator"
     \/ lease.holder \in paused
  /\ lease' = [lease EXCEPT !.expired = TRUE]
  /\ UNCHANGED << rec, resources, vars_node, envCtr, failCtr >>

(* A keep-alive expires when its node no longer sends it: it is off or (LongPauses) stalled.
   A coordinator sends none while waiting at the barrier, which is assumed to take less than
   the expiry: a client that does not confirm stops sending keep-alives too and drops out. *)
KeepAliveExpire(n) ==
  /\ rec[n].present
  /\ rec[n].fresh
  /\ \/ role[n] = "off"
     \/ n \in paused
  /\ rec' = [rec EXCEPT ![n].fresh = FALSE]
  /\ UNCHANGED << lease, resources, vars_node, envCtr, failCtr >>

---------------------------------------------------------
(* ENVIRONMENT: nodes and resources come and go *)

(* StartAsync: a new client id and record. A restarted process gets a new client id, and a
   cleanly stopped one is never used again (ADR-0014), so each id starts at most once. *)
StartNode(n) ==
  /\ envCtr < MaxEnvEvents
  /\ role[n] = "new"
  /\ rec' = [rec EXCEPT ![n] = NewRecord]
  /\ role' = [role EXCEPT ![n] = "none"]
  /\ myToken' = [myToken EXCEPT ![n] = 0]
  /\ known' = [known EXCEPT ![n] = NoKnown]
  /\ stoppedFlag' = [stoppedFlag EXCEPT ![n] = FALSE]
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << lease, resources, rb, rbRes, rbToken, access, paused, lagging, failCtr >>

(* A clean stop: the handlers stop, the record is deleted (ADR-0014) and a coordinator
   relinquishes its lease *)
StopNode(n) ==
  /\ envCtr < MaxEnvEvents
  /\ Running(n)
  /\ access' = [access EXCEPT ![n] = {}]
  /\ rec' = [rec EXCEPT ![n] = NoRecord]
  /\ lease' = IF role[n] = "coordinator" THEN RelinquishedLease(n, myToken[n]) ELSE lease
  /\ role' = [role EXCEPT ![n] = "off"]
  /\ rb' = [rb EXCEPT ![n] = "idle"]
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << resources, myToken, rbRes, rbToken, known, stoppedFlag, paused, lagging, failCtr >>

(* The process dies: its handlers die with it; its record and lease stay until they expire *)
CrashNode(n) ==
  /\ envCtr < MaxEnvEvents
  /\ role[n] \notin { "new", "off" }
  /\ access' = [access EXCEPT ![n] = {}]
  /\ role' = [role EXCEPT ![n] = "off"]
  /\ rb' = [rb EXCEPT ![n] = "idle"]
  /\ paused' = paused \ {n}
  /\ lagging' = lagging \ {n}
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << vars_store, myToken, rbRes, rbToken, known, stoppedFlag, failCtr >>

AddResource ==
  /\ envCtr < MaxEnvEvents
  /\ \E r \in R \ resources : resources' = resources \cup {r}
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << lease, rec, vars_node, failCtr >>

RemoveResource ==
  /\ envCtr < MaxEnvEvents
  /\ Cardinality(resources) > 1
  /\ \E r \in resources : resources' = resources \ {r}
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << lease, rec, vars_node, failCtr >>

EnvironmentChange ==
  \/ \E n \in N : StartNode(n) \/ StopNode(n) \/ CrashNode(n)
  \/ AddResource
  \/ RemoveResource

---------------------------------------------------------
(* FAILURES *)

(* A store error faults a task: OnAborted, OnStop (sets stoppedDueToInternalErrorFlag), the
   record is reset as a new registration would write it (PRB-0005; a custom client service still
   marks it Terminated, not modelled), a coordinator relinquishes its lease, and the tasks restart
   (ClientOptions.AutoRecoveryOnError) *)
StoreError(n) ==
  /\ failCtr < MaxFailures
  /\ Running(n)
  /\ access' = [access EXCEPT ![n] = {}]
  /\ stoppedFlag' = [stoppedFlag EXCEPT ![n] = TRUE]
  /\ rec' = IF rec[n].present THEN [rec EXCEPT ![n].status = "waiting", ![n].cmd = "stop", ![n].res = {}] ELSE rec
  /\ lease' = IF role[n] = "coordinator" THEN RelinquishedLease(n, myToken[n]) ELSE lease
  /\ role' = [role EXCEPT ![n] = "none"]
  /\ rb' = [rb EXCEPT ![n] = "idle"]
  /\ failCtr' = failCtr + 1
  /\ UNCHANGED << resources, myToken, rbRes, rbToken, known, paused, lagging, envCtr >>

(* A stall longer than the lease / keep-alive expiry (GC pause, VM freeze), only with LongPauses *)
Pause(n) ==
  /\ LongPauses
  /\ failCtr < MaxFailures
  /\ Running(n)
  /\ paused' = paused \cup {n}
  /\ failCtr' = failCtr + 1
  /\ UNCHANGED << vars_store, role, myToken, rb, rbRes, rbToken, known, stoppedFlag, access, lagging, envCtr >>

Resume(n) ==
  /\ n \in paused
  /\ paused' = paused \ {n}
  /\ lagging' = lagging \cup {n}
  /\ UNCHANGED << vars_store, role, myToken, rb, rbRes, rbToken, known, stoppedFlag, access, envCtr, failCtr >>

---------------------------------------------------------
(* NEXT AND TEMPORAL FORMULA *)

NodeAction(n) ==
  \/ AcquireLeadership(n)
  \/ DenyLeadership(n)
  \/ Demote(n)
  \/ CoordinatorTick(n)
  \/ CoordinatorGrant(n)
  \/ CoordinatorResendStop(n)
  \/ CoordinatorExpireRecord(n)
  \/ FollowerTick(n)
  \/ Resume(n)

Failure(n) ==
  \/ StoreError(n)
  \/ Pause(n)

Next ==
  \/ EnvironmentChange
  \/ \E n \in N : NodeAction(n) \/ Failure(n) \/ KeepAliveExpire(n)
  \/ LeaseExpire

(* Heartbeats keep running and time keeps passing; the environment and failures are bounded *)
Fairness ==
  /\ \A n \in N : WF_vars(NodeAction(n))
  /\ \A n \in N : WF_vars(KeepAliveExpire(n))
  /\ WF_vars(LeaseExpire)

Spec == Init /\ [][Next]_vars /\ Fairness

(* Once the environment is quiet, the group converges to a complete assignment *)
EventuallyConverged == <>[]Converged

(* Any double access ends: the bound that remains when nodes stall longer than the expiry *)
EventuallyNoDoubleAccess == <>[]NoDoubleAccess

(* Checked by TLC within the bounds of the .cfg files, not proved *)
THEOREM Spec => [](TypeOK /\ OneLeaseHolder) /\ EventuallyNoDoubleAccess
THEOREM ~LongPauses => (Spec => []NoDoubleAccess)

=============================================================================
