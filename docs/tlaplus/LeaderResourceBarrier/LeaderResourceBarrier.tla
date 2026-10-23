----------------------- MODULE LeaderResourceBarrier -----------------------
(* ZooKeeper RebalancingMode.ResourceBarrier (ADR-0003), as implemented in
   src/Rebalancer.ZooKeeper (ZooKeeperProvider.cs, ResourceBarrier/Coordinator.cs,
   ResourceBarrier/Follower.cs, ResourceManagement/ResourceManager.cs):
   - the lowest client znode is the coordinator; it increments the epoch and writes the assignment
     table with its expected version
   - on a new table, every client stops its handlers, deletes the barriers of the resources it ran,
     then creates a barrier on each newly assigned resource (waiting while another client holds
     it) and starts its handlers
   - a newer table cancels a rebalancing in progress
   A process keeps its ResourceManager (the resources it last started) across sessions. *)
EXTENDS Integers, FiniteSets, TLC, RebalancerStore
CONSTANTS P,                 \* the set of processes
          LateExpiryNotice,  \* TRUE: ZooKeeper can expire a session before its process notices
          MaxSessions,       \* bound on client znodes (sessions) for model checking
          MaxEnvEvents,      \* bound on process starts, stops, crashes and resource changes
          MaxFailures        \* bound on disconnections and errors
VARIABLES role,      \* "new", "nosession", "none" (no role yet), "follower", "coordinator", "off"
          sid,       \* the process's current (or last) session, 0 before the first
          conn,      \* "up" or "down": ZooKeeper operations wait while disconnected
          myEpoch,   \* the epoch the coordinator set
          resVer,    \* the resources znode version the coordinator expects
          lastOk,    \* the coordinator's last rebalancing completed
          trig,      \* a clients or resources children watch fired for the coordinator
          notified,  \* the resources data watch fired for the follower
          phase,     \* "idle" or "acquiring" (in OnStart, creating barriers)
          want,      \* the resources being acquired
          local,     \* ResourceManager's resources: started, and not yet stopped with success
          assigned,  \* ResourceManager's status is not NoAssignmentYet
          access,    \* the resources whose handlers run (between OnStart and OnStop)
          envCtr,    \* number of environment events
          failCtr    \* number of failures

vars_store == << clients, nextSeq, epoch, resources, assign, aVer, barrier >>
vars_proc == << role, sid, conn, myEpoch, resVer, lastOk, trig, notified, phase, want, local, assigned, access >>
vars == << vars_store, vars_proc, envCtr, failCtr >>

Role == { "new", "nosession", "none", "follower", "coordinator", "off" }
Running == { "none", "follower", "coordinator" }

---------------------------------------------------------
(* INIT AND INVARIANTS *)

Init ==
  /\ StoreInit
  /\ role = [p \in P |-> "new"]
  /\ sid = [p \in P |-> 0]
  /\ conn = [p \in P |-> "up"]
  /\ myEpoch = [p \in P |-> 0]
  /\ resVer = [p \in P |-> 0]
  /\ lastOk = [p \in P |-> FALSE]
  /\ trig = [p \in P |-> FALSE]
  /\ notified = [p \in P |-> FALSE]
  /\ phase = [p \in P |-> "idle"]
  /\ want = [p \in P |-> {}]
  /\ local = [p \in P |-> {}]
  /\ assigned = [p \in P |-> FALSE]
  /\ access = [p \in P |-> {}]
  /\ envCtr = 0
  /\ failCtr = 0

TypeOK ==
  /\ StoreTypeOK
  /\ role \in [P -> Role]
  /\ sid \in [P -> Nat]
  /\ conn \in [P -> { "up", "down" }]
  /\ myEpoch \in [P -> Nat]
  /\ resVer \in [P -> Nat]
  /\ lastOk \in [P -> BOOLEAN]
  /\ trig \in [P -> BOOLEAN]
  /\ notified \in [P -> BOOLEAN]
  /\ phase \in [P -> { "idle", "acquiring" }]
  /\ want \in [P -> SUBSET R]
  /\ local \in [P -> SUBSET R]
  /\ assigned \in [P -> BOOLEAN]
  /\ access \in [P -> SUBSET R]

(* Two processes never run the handlers of the same resource *)
NoDoubleAccess ==
  \A r \in R : Cardinality({ p \in P : r \in access[p] }) <= 1

(* A process runs a resource only while its session holds the barrier *)
AccessHoldsBarrier ==
  \A p \in P : \A r \in access[p] : barrier[r] = sid[p]

(* Once the environment is quiet: every connected process with a role is idle, runs what the
   table assigns to its session, and every resource is run *)
Converged ==
  LET up == { p \in P : role[p] \in { "follower", "coordinator" } }
  IN /\ \A p \in P : role[p] \notin { "nosession", "none" }
     /\ \A p \in up : /\ conn[p] = "up"
                      /\ phase[p] = "idle"
                      /\ access[p] = AssignedTo(sid[p])
     /\ up # {} => \A r \in resources : \E p \in up : r \in access[p]

---------------------------------------------------------
(* HELPERS *)

(* ZooKeeper operations are possible: the process has a role and is connected (a connected
   session has not expired) *)
Connected(p) ==
  /\ role[p] \in Running
  /\ conn[p] = "up"

(* A children watch fires for the coordinator *)
NotifyCoordinators ==
  trig' = [q \in P |-> trig[q] \/ role[q] = "coordinator"]

(* CleanUpAsync on a live session: a rebalancing in progress is cancelled, which removes the
   barriers of all resources being acquired; OnStop stops the handlers and removes the barriers of
   the resources it ran. Each removal deletes a barrier only if this client holds it (ADR-0027). *)
\* ADR-0027: a client deletes only the barriers its session holds
DeleteFor(p, res) ==
  DeleteOwnedBarriers(res, sid[p])

CleanUpBarriers(p) ==
  DeleteFor(p, (IF phase[p] = "acquiring" THEN want[p] ELSE {}) \cup local[p])

StopLocally(p) ==
  /\ access' = [access EXCEPT ![p] = {}]
  /\ local' = [local EXCEPT ![p] = {}]
  /\ assigned' = [assigned EXCEPT ![p] = FALSE]

(* An even split of the resources over the client znodes (zombie sessions included) *)
Balanced(alloc, C, res) ==
  /\ \A m, o \in C : m = o \/ alloc[m] \cap alloc[o] = {}
  /\ UNION { alloc[m] : m \in C } = res
  /\ \A m, o \in C : Cardinality(alloc[m]) - Cardinality(alloc[o]) \in { -1, 0, 1 }

Allocation ==
  LET alloc == CHOOSE a \in [clients -> SUBSET resources] : Balanced(a, clients, resources)
  IN [r \in R |-> IF r \in resources THEN CHOOSE s \in clients : r \in alloc[s] ELSE 0]

(* Coordinator.IsRebalancingRequired *)
RebalancingRequired(p) ==
  \/ ~lastOk[p]
  \/ ~assigned[p]
  \/ { r \in R : assign[r] # 0 } # resources
  \/ Cardinality(clients) < Cardinality(resources)
  \/ \E r1, r2 \in R : r1 # r2 /\ assign[r1] # 0 /\ assign[r1] = assign[r2]
  \/ \E r \in R : assign[r] # 0 /\ assign[r] \notin clients

---------------------------------------------------------
(* SESSION AND ELECTION (ZooKeeperProvider.RunStateMachine) *)

StartProcess(p) ==
  /\ envCtr < MaxEnvEvents
  /\ role[p] = "new"
  /\ role' = [role EXCEPT ![p] = "nosession"]
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << vars_store, sid, conn, myEpoch, resVer, lastOk, trig, notified, phase, want, local,
                  assigned, access, failCtr >>

(* EstablishSessionAsync closes the previous session (deleting its ephemeral znodes), then
   CreateClientNodeAsync adds an ephemeral sequential client znode *)
NewSession(p) ==
  LET old == sid[p]
      s == nextSeq
  IN /\ role[p] = "nosession"
     /\ nextSeq <= MaxSessions
     /\ clients' = (clients \ {old}) \cup {s}
     /\ barrier' = WithoutSession(old)
     /\ nextSeq' = nextSeq + 1
     /\ sid' = [sid EXCEPT ![p] = s]
     /\ conn' = [conn EXCEPT ![p] = "up"]
     /\ role' = [role EXCEPT ![p] = "none"]
     /\ NotifyCoordinators
     /\ UNCHANGED << epoch, resources, assign, aVer, myEpoch, resVer, lastOk, notified, phase, want, local,
                     assigned, access, envCtr, failCtr >>

(* The lowest client znode becomes the coordinator: it increments the epoch, caches the resources
   znode version, and triggers a rebalancing. Others become followers, watch the resources data
   and respond at once if the table already assigns them resources (CheckForRebalancingAsync). *)
Elect(p) ==
  /\ Connected(p)
  /\ role[p] = "none"
  /\ IF IsLowest(sid[p])
       THEN /\ role' = [role EXCEPT ![p] = "coordinator"]
            /\ epoch' = epoch + 1
            /\ myEpoch' = [myEpoch EXCEPT ![p] = epoch + 1]
            /\ resVer' = [resVer EXCEPT ![p] = aVer]
            /\ trig' = [trig EXCEPT ![p] = TRUE]
            /\ lastOk' = [lastOk EXCEPT ![p] = FALSE]
            /\ UNCHANGED notified
       ELSE /\ role' = [role EXCEPT ![p] = "follower"]
            /\ notified' = [notified EXCEPT ![p] = AssignedTo(sid[p]) # {}]
            /\ UNCHANGED << epoch, myEpoch, resVer, trig, lastOk >>
  /\ UNCHANGED << clients, nextSeq, resources, assign, aVer, barrier, sid, conn, phase, want, local,
                  assigned, access, envCtr, failCtr >>

---------------------------------------------------------
(* COORDINATOR (ResourceBarrier/Coordinator.cs) *)

(* A triggered rebalancing cancels the one in progress, then:
   - a changed resources znode version means another coordinator: NoLongerCoordinator
   - if not required (IsRebalancingRequired), nothing changes
   - else write the table with the expected version and run OnStop, then OnStart for its own share *)
CoordinatorRebalance(p) ==
  /\ Connected(p)
  /\ role[p] = "coordinator"
  /\ trig[p]
  /\ trig' = [trig EXCEPT ![p] = FALSE]
  /\ CASE aVer # resVer[p] ->
            /\ barrier' = CleanUpBarriers(p)
            /\ StopLocally(p)
            /\ role' = [role EXCEPT ![p] = "nosession"]
            /\ phase' = [phase EXCEPT ![p] = "idle"]
            /\ want' = [want EXCEPT ![p] = {}]
            /\ UNCHANGED << assign, aVer, resVer, lastOk, notified >>
       [] phase[p] = "idle" /\ ~RebalancingRequired(p) ->
            /\ lastOk' = [lastOk EXCEPT ![p] = TRUE]
            /\ UNCHANGED << barrier, assign, aVer, resVer, notified, role, phase, want, local, assigned, access >>
       [] OTHER ->
            LET table == Allocation
            IN /\ assign' = table
               /\ aVer' = aVer + 1
               /\ resVer' = [resVer EXCEPT ![p] = aVer + 1]
               /\ notified' = [q \in P |-> notified[q] \/ (role[q] = "follower" /\ sid[q] \in clients)]
               /\ barrier' = CleanUpBarriers(p)
               /\ StopLocally(p)
               /\ phase' = [phase EXCEPT ![p] = "acquiring"]
               /\ want' = [want EXCEPT ![p] = { r \in R : table[r] = sid[p] }]
               /\ lastOk' = [lastOk EXCEPT ![p] = FALSE]
               /\ UNCHANGED role
  /\ UNCHANGED << clients, nextSeq, epoch, resources, sid, conn, myEpoch, envCtr, failCtr >>

(* The epoch watch fires: another client became coordinator. CleanUpAsync, then a new session. *)
CoordinatorDemote(p) ==
  /\ Connected(p)
  /\ role[p] = "coordinator"
  /\ epoch # myEpoch[p]
  /\ barrier' = CleanUpBarriers(p)
  /\ StopLocally(p)
  /\ role' = [role EXCEPT ![p] = "nosession"]
  /\ phase' = [phase EXCEPT ![p] = "idle"]
  /\ want' = [want EXCEPT ![p] = {}]
  /\ UNCHANGED << clients, nextSeq, epoch, resources, assign, aVer, sid, conn, myEpoch, resVer, lastOk, trig,
                  notified, envCtr, failCtr >>

---------------------------------------------------------
(* FOLLOWER (ResourceBarrier/Follower.cs) *)

(* The resources data watch fired (a follower), or the client reconnected after a suspension
   (ADR-0028, a follower or the coordinator): cancel the rebalancing in progress, OnStop, read the
   table, and start acquiring the barriers of the assigned resources *)
Respond(p) ==
  /\ Connected(p)
  /\ role[p] \in { "follower", "coordinator" }
  /\ notified[p]
  /\ notified' = [notified EXCEPT ![p] = FALSE]
  /\ barrier' = CleanUpBarriers(p)
  /\ StopLocally(p)
  /\ phase' = [phase EXCEPT ![p] = "acquiring"]
  /\ want' = [want EXCEPT ![p] = AssignedTo(sid[p])]
  /\ UNCHANGED << clients, nextSeq, epoch, resources, assign, aVer, role, sid, conn, myEpoch, resVer, lastOk,
                  trig, envCtr, failCtr >>

(* The watched lower sibling is gone and this client is now the lowest: CleanUpAsync, then
   the election runs again on the same session *)
FollowerPromote(p) ==
  /\ Connected(p)
  /\ role[p] = "follower"
  /\ IsLowest(sid[p])
  /\ barrier' = CleanUpBarriers(p)
  /\ StopLocally(p)
  /\ role' = [role EXCEPT ![p] = "none"]
  /\ phase' = [phase EXCEPT ![p] = "idle"]
  /\ want' = [want EXCEPT ![p] = {}]
  /\ notified' = [notified EXCEPT ![p] = FALSE]
  /\ UNCHANGED << clients, nextSeq, epoch, resources, assign, aVer, sid, conn, myEpoch, resVer, lastOk, trig,
                  envCtr, failCtr >>

---------------------------------------------------------
(* ONSTART (ResourceManager.InvokeOnStartActionsAsync), coordinator and follower alike *)

(* TryPutResourceBarrierAsync: create the barrier znode, or wait while another client holds it.
   If the resource znode is gone the call fails: PotentialInconsistentState, CleanUpAsync (the
   rebalancing already ended, so nothing is cancelled) and, with auto-recovery, a new session. *)
AcquireBarrier(p) ==
  /\ Connected(p)
  /\ role[p] \in { "follower", "coordinator" }
  /\ phase[p] = "acquiring"
  /\ \E r \in want[p] :
       /\ barrier[r] # sid[p]
       /\ IF r \in resources
            THEN /\ barrier[r] = 0
                 /\ barrier' = [barrier EXCEPT ![r] = sid[p]]
                 /\ UNCHANGED << role, phase, want, local, assigned, access >>
            ELSE /\ barrier' = DeleteFor(p, local[p])
                 /\ StopLocally(p)
                 /\ role' = [role EXCEPT ![p] = "nosession"]
                 /\ phase' = [phase EXCEPT ![p] = "idle"]
                 /\ want' = [want EXCEPT ![p] = {}]
  /\ UNCHANGED << clients, nextSeq, epoch, resources, assign, aVer, sid, conn, myEpoch, resVer, lastOk, trig,
                  notified, envCtr, failCtr >>

(* All barriers placed: record the resources and start the handlers *)
FinishStart(p) ==
  /\ role[p] \in { "follower", "coordinator" }
  /\ phase[p] = "acquiring"
  /\ conn[p] = "up"  \* ADR-0028: no handler starts while disconnected
  /\ \A r \in want[p] : barrier[r] = sid[p]
  /\ phase' = [phase EXCEPT ![p] = "idle"]
  /\ local' = [local EXCEPT ![p] = want[p]]
  /\ access' = [access EXCEPT ![p] = want[p]]
  /\ assigned' = [assigned EXCEPT ![p] = TRUE]
  /\ lastOk' = [lastOk EXCEPT ![p] = (role[p] = "coordinator") \/ lastOk[p]]
  /\ UNCHANGED << vars_store, role, sid, conn, myEpoch, resVer, trig, notified, want, envCtr, failCtr >>

---------------------------------------------------------
(* ENVIRONMENT *)

(* TerminateAsync: the session is closed first (its ephemeral znodes go), then OnStop *)
StopProcess(p) ==
  /\ envCtr < MaxEnvEvents
  /\ role[p] \in Running \cup { "nosession" }
  /\ clients' = clients \ {sid[p]}
  /\ barrier' = WithoutSession(sid[p])
  /\ access' = [access EXCEPT ![p] = {}]
  /\ role' = [role EXCEPT ![p] = "off"]
  /\ NotifyCoordinators
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << nextSeq, epoch, resources, assign, aVer, sid, conn, myEpoch, resVer, lastOk, notified,
                  phase, want, local, assigned, failCtr >>

(* The process dies with its handlers; its session stays until ZooKeeper expires it *)
CrashProcess(p) ==
  /\ envCtr < MaxEnvEvents
  /\ role[p] \in Running \cup { "nosession" }
  /\ access' = [access EXCEPT ![p] = {}]
  /\ role' = [role EXCEPT ![p] = "off"]
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << vars_store, sid, conn, myEpoch, resVer, lastOk, trig, notified, phase, want, local,
                  assigned, failCtr >>

AddResource ==
  /\ envCtr < MaxEnvEvents
  /\ \E r \in R \ resources : resources' = resources \cup {r}
  /\ NotifyCoordinators
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << clients, nextSeq, epoch, assign, aVer, barrier, role, sid, conn, myEpoch, resVer, lastOk,
                  notified, phase, want, local, assigned, access, failCtr >>

(* ZooKeeper deletes a znode only without children: a resource with a barrier cannot be deleted *)
RemoveResource ==
  /\ envCtr < MaxEnvEvents
  /\ \E r \in resources : /\ barrier[r] = 0
                          /\ resources' = resources \ {r}
  /\ NotifyCoordinators
  /\ envCtr' = envCtr + 1
  /\ UNCHANGED << clients, nextSeq, epoch, assign, aVer, barrier, role, sid, conn, myEpoch, resVer, lastOk,
                  notified, phase, want, local, assigned, access, failCtr >>

EnvironmentChange ==
  \/ \E p \in P : StartProcess(p) \/ StopProcess(p) \/ CrashProcess(p)
  \/ AddResource
  \/ RemoveResource

---------------------------------------------------------
(* FAILURES *)

Disconnect(p) ==
  /\ failCtr < MaxFailures
  /\ Connected(p)
  /\ conn' = [conn EXCEPT ![p] = "down"]
  \* ADR-0028: the stop actions run at Disconnected; barriers and ResourceManager's resources stay
  /\ access' = [access EXCEPT ![p] = {}]
  /\ failCtr' = failCtr + 1
  /\ UNCHANGED << vars_store, role, sid, myEpoch, resVer, lastOk, trig, notified, phase, want, local,
                  assigned, envCtr >>

Reconnect(p) ==
  /\ role[p] \in Running
  /\ conn[p] = "down"
  /\ sid[p] \in clients
  /\ conn' = [conn EXCEPT ![p] = "up"]
  \* ADR-0028: a reconnect with the same session is a rebalancing event: the client responds to the
  \* current table again (a coordinator for its own share only), re-acquiring its own barriers
  /\ notified' = [notified EXCEPT ![p] = TRUE]
  /\ UNCHANGED << vars_store, role, sid, myEpoch, resVer, lastOk, trig, phase, want, local,
                  assigned, access, envCtr, failCtr >>

(* The disconnected timer (a third of the session timeout) or the Expired event: the process
   treats the session as expired. CleanUpAsync stops the handlers; every barrier deletion fails on
   the expired session, and ResourceManager forgets its resources anyway (ADR-0027). Then a
   new session. *)
SessionLost(p) ==
  /\ role[p] \in Running
  /\ conn[p] = "down"
  /\ access' = [access EXCEPT ![p] = {}]
  /\ local' = [local EXCEPT ![p] = {}]
  /\ assigned' = [assigned EXCEPT ![p] = FALSE]
  /\ role' = [role EXCEPT ![p] = "nosession"]
  /\ phase' = [phase EXCEPT ![p] = "idle"]
  /\ want' = [want EXCEPT ![p] = {}]
  /\ notified' = [notified EXCEPT ![p] = FALSE]
  /\ UNCHANGED << vars_store, sid, conn, myEpoch, resVer, lastOk, trig, envCtr, failCtr >>

(* An error that ends the role (PotentialInconsistentState): CleanUpAsync on the live session,
   then auto-recovery starts a new session *)
ErrorRestart(p) ==
  /\ failCtr < MaxFailures
  /\ Connected(p)
  /\ barrier' = CleanUpBarriers(p)
  /\ StopLocally(p)
  /\ role' = [role EXCEPT ![p] = "nosession"]
  /\ phase' = [phase EXCEPT ![p] = "idle"]
  /\ want' = [want EXCEPT ![p] = {}]
  /\ failCtr' = failCtr + 1
  /\ UNCHANGED << clients, nextSeq, epoch, resources, assign, aVer, sid, conn, myEpoch, resVer, lastOk, trig,
                  notified, envCtr >>

(* ZooKeeper expires a session whose process no longer heartbeats: it crashed or moved to a new
   session, or (LateExpiryNotice) it is disconnected and has not noticed yet *)
ServerExpire ==
  \E s \in clients :
    /\ \A p \in P : sid[p] = s => \/ role[p] \in { "nosession", "off" }
                                  \/ LateExpiryNotice /\ conn[p] = "down"
    /\ clients' = clients \ {s}
    /\ barrier' = WithoutSession(s)
    /\ NotifyCoordinators
    /\ UNCHANGED << nextSeq, epoch, resources, assign, aVer, role, sid, conn, myEpoch, resVer, lastOk, notified,
                    phase, want, local, assigned, access, envCtr, failCtr >>

---------------------------------------------------------
(* NEXT AND TEMPORAL FORMULA *)

ProcessAction(p) ==
  \/ NewSession(p)
  \/ Elect(p)
  \/ CoordinatorRebalance(p)
  \/ CoordinatorDemote(p)
  \/ Respond(p)
  \/ FollowerPromote(p)
  \/ AcquireBarrier(p)
  \/ FinishStart(p)
  \/ SessionLost(p)

Next ==
  \/ EnvironmentChange
  \/ \E p \in P : ProcessAction(p) \/ Disconnect(p) \/ Reconnect(p) \/ ErrorRestart(p)
  \/ ServerExpire

Fairness ==
  /\ \A p \in P : WF_vars(ProcessAction(p))
  /\ WF_vars(ServerExpire)

Spec == Init /\ [][Next]_vars /\ Fairness

EventuallyConverged == <>[]Converged

=============================================================================
