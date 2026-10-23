------------------------- MODULE RebalancerStore ---------------------------
(* The ZooKeeper znodes of a resource group in RebalancingMode.ResourceBarrier (ADR-0003),
   as written by src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs:
   - {group}/clients/c_*         ephemeral sequential, one per session (the number is the session)
   - {group}/epoch               its version increments once per coordinator tenure
   - {group}/resources           children: the resources; data: the assignment table, written with
                                 its expected version
   - {group}/resources/{r}/barrier  ephemeral, holds the id of the client that owns resource r
   A session is a client znode; ZooKeeper deletes its ephemeral znodes when it closes or expires. *)
EXTENDS Naturals, FiniteSets
CONSTANTS R  \* the set of all possible resources
VARIABLES clients,   \* the sessions with a client znode
          nextSeq,   \* the next sequence number ZooKeeper appends to a client znode
          epoch,     \* the version of the epoch znode
          resources, \* the children of the resources znode
          assign,    \* the assignment table: a function of resource to session (0: unassigned)
          aVer,      \* the version of the resources znode's data
          barrier    \* a function of resource to the session owning its barrier znode (0: none)

StoreInit ==
  /\ clients = {}
  /\ nextSeq = 1
  /\ epoch = 0
  /\ resources = R
  /\ assign = [r \in R |-> 0]
  /\ aVer = 0
  /\ barrier = [r \in R |-> 0]

StoreTypeOK ==
  /\ clients \subseteq Nat \ {0}
  /\ nextSeq \in Nat \ {0}
  /\ epoch \in Nat
  /\ resources \subseteq R
  /\ assign \in [R -> Nat]
  /\ aVer \in Nat
  /\ barrier \in [R -> Nat]

(* The lowest client znode is the leader (DetermineLeadershipAsync) *)
IsLowest(s) ==
  /\ s \in clients
  /\ \A c \in clients : s <= c

(* Closing or expiring a session deletes its client znode and its barrier znodes *)
WithoutSession(s) ==
  [r \in R |-> IF barrier[r] = s THEN 0 ELSE barrier[r]]

(* RemoveResourceBarrierAsync (ADR-0027): delete only the barriers session s owns. The implementation
   compares the barrier's data with the client id, which is one per session. *)
DeleteOwnedBarriers(res, s) ==
  [r \in R |-> IF r \in res /\ barrier[r] = s THEN 0 ELSE barrier[r]]

(* The resources a session is assigned in the table *)
AssignedTo(s) ==
  { r \in R : assign[r] = s }

=============================================================================
