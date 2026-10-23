--------------------------- MODULE RebalancerStore ---------------------------
(* The coordination store shared by the SQL Server and Redis backends (ADR-0002):
   - a lease per resource group, carrying the coordinator and its fencing token (RBR.ResourceGroups / RBR:Lease:{g})
   - one client record per node (RBR.Clients / RBR:Clients:{g})
   - the resource identifiers of the group (RBR.Resources / RBR:Resources:{g})
   Time is not modelled: a lease or a keep-alive "expires" through an explicit action of the
   protocol module, which states when that is allowed. *)
EXTENDS Naturals, FiniteSets
CONSTANTS N,   \* the set of all possible client ids (one per process run)
          R,   \* the set of all possible resources
          Nil  \* no node
VARIABLES lease,     \* [holder, token, expired]: the coordinator lease and its fencing token
          rec,       \* a function of node to its client record
          resources  \* the resources of the group

Status == { "waiting", "active", "terminated" }  \* ClientStatus, written by the node itself
Command == { "stop", "granted" }                 \* CoordinatorStatus, written by the coordinator

Record == [present : BOOLEAN, status : Status \cup {"-"}, cmd : Command,
           res : SUBSET R, token : Nat, fresh : BOOLEAN]

(* No row / no hash field *)
NoRecord == [present |-> FALSE, status |-> "-", cmd |-> "stop", res |-> {}, token |-> 0, fresh |-> FALSE]

(* ClientService.CreateClientAsync *)
NewRecord == [present |-> TRUE, status |-> "waiting", cmd |-> "stop", res |-> {}, token |-> 1, fresh |-> TRUE]

StoreInit ==
  /\ lease = [holder |-> Nil, token |-> 0, expired |-> FALSE]
  /\ rec = [n \in N |-> NoRecord]
  /\ resources = R

StoreTypeOK ==
  /\ lease \in [holder : N \cup {Nil}, token : Nat, expired : BOOLEAN]
  /\ rec \in [N -> Record]
  /\ resources \subseteq R

---------------------------------------------------------
(* LEASE (LeaseService) *)

(* TryAcquireLease grants when there is no coordinator or its lease expired *)
LeaseFree ==
  lease.holder = Nil \/ lease.expired

(* A grant increments the fencing token *)
AcquiredLease(n) ==
  [holder |-> n, token |-> lease.token + 1, expired |-> FALSE]

(* TryRenewLease grants only to the holder of the latest fencing token whose lease has not expired *)
HoldsLease(n, t) ==
  /\ lease.holder = n
  /\ lease.token = t
  /\ ~lease.expired

(* The guard of a fenced coordinator write (ADR-0014's DeleteExpiredClientsAsync, and ADR-0026): the lease row
   still names this coordinator and fencing token. Expiry needs no check: another node must write the lease row
   before it can act. *)
LeaseGuard(n, t) ==
  /\ lease.holder = n
  /\ lease.token = t

(* RelinquishLease clears the coordinator iff the holder and fencing token match *)
RelinquishedLease(n, t) ==
  IF lease.holder = n /\ lease.token = t
    THEN [lease EXCEPT !.holder = Nil]
    ELSE lease

---------------------------------------------------------
(* CLIENT RECORDS (ClientService) *)

(* GetActiveClientsAsync filtered by keep-alive expiry: the coordinator's view of the live clients *)
LiveClients(self) ==
  { m \in N \ {self} : /\ rec[m].present
                       /\ rec[m].fresh
                       /\ rec[m].status \in { "waiting", "active" } }

(* ADR-0019: clients whose keep-alive expired while their record still grants resources *)
UnresponsiveClients(self) ==
  { m \in N \ {self} : /\ rec[m].present
                       /\ ~rec[m].fresh
                       /\ rec[m].status \in { "waiting", "active" }
                       /\ rec[m].cmd = "granted" }

(* A coordinator write with fencing token t succeeds on a record iff the record exists and
   has not seen a newer token *)
Fenced(C, t) ==
  \A m \in C : rec[m].present /\ rec[m].token <= t

(* KeepAliveAsync: the record with a refreshed keep-alive, or a new record when it expired
   and was deleted (ADR-0014, re-registration) *)
KeptAlive(n) ==
  IF rec[n].present THEN [rec[n] EXCEPT !.fresh = TRUE] ELSE NewRecord

(* StopActivityAsync: the records with token <= t are updated (SQL Server updates them in one
   statement and reports a violation when the row count differs; Redis writes all or none,
   which is one of the behaviours allowed here) *)
StoppedRecords(C, t) ==
  [m \in N |-> IF m \in C /\ rec[m].present /\ rec[m].token <= t
                 THEN [rec[m] EXCEPT !.cmd = "stop", !.res = {}, !.token = t]
                 ELSE rec[m]]

(* StartActivityAsync: all records or none (a transaction); the caller checks Fenced first *)
GrantedRecords(alloc, t) ==
  [m \in N |-> IF m \in DOMAIN alloc
                 THEN [rec[m] EXCEPT !.cmd = "granted", !.res = alloc[m], !.token = t]
                 ELSE rec[m]]

=============================================================================
