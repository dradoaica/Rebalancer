---
id: ADR-0037
status: Accepted
date: 2026-11-02
decides: "how the Redis backend makes its conditional writes without watching whole hashes (PRB-0009, 6.0)"
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only (dradoaica, in session 2026-11-02)
decision-makers: [ "dradoaica" ]
consulted: [ ]
informed: [ ]
depends-on: [ "ADR-0017", "ADR-0020", "ADR-0026", "ADR-0034", "ADR-0036", "TM-0003", "PRB-0009" ]
supersedes: [ ]
superseded-by: ""
---

# Redis guarded writes as server-side scripts

## Context and Problem Statement

Every conditional write of the Redis backend is an optimistic transaction (ADR-0017): read, decide, then `MULTI/EXEC`
under `Condition.HashEqual`, retried up to 10 times. StackExchange.Redis implements those conditions with `WATCH`, and
`WATCH` covers a whole key. A group's client records share one hash and its lease is one hash (ADR-0020), so a write
to any field aborts every transaction in flight on that key: each node's keep-alive aborts the others' keep-alives and
the coordinator's writes, and the coordinator's lease renewals abort its own client writes. Over a slow link nearly
every attempt meets such a write, a loop runs out of attempts, the node restarts, and the group may not settle
(PRB-0009). ADR-0036 (5.2.0) made the coordinator's writes per record and cut the failures of the reproducing test
from 3 in 6 to 2 in 26, but did not remove them: in a traced failure no client write ran out of attempts, which
points at the keep-alive and lease loops.

This record decides how the Redis backend makes its conditional writes so that unrelated writes to the same hash do
not make them fail, without weakening the lease guard (ADR-0026) or the fencing checks.

## Considered Options

* **A. Server-side scripts:** each conditional write (keep-alive, lease acquire / renew / relinquish, the
  coordinator's guarded client writes, the expiry deletion) is a Lua script run with `EVALSHA` (StackExchange.Redis
  `LuaScript`, which loads it on first use). The script reads the fields it needs, checks the condition (lease holder
  and token, the record's token and existence) and writes in one atomic step, with the server time from `TIME`. No
  `WATCH`, no retries, one round trip. Keys are passed as `KEYS`, so ACL key patterns keep applying.
* **B. One key per client record** (`RBR:Client:{group}:{id}`): `WATCH` then covers one record, so nodes' keep-alives
  no longer conflict with each other; a record still conflicts with its own node's keep-alive and the lease with its
  renewals. Changes the stored layout (a migration like ADR-0020's) and the ACL key patterns.
* **C. Redis Functions** (`FUNCTION LOAD` / `FCALL`, Redis 7 and Valkey): as A, but the library lives in the server
  and an operator loads it, an extra provisioning step per server and version.
* **D. More attempts or a time-bounded retry:** no change to commands, layout or ACL; each attempt's odds stay the
  same, so it narrows the problem rather than removing it.

## Decision Outcome

Chosen option: "A. Server-side scripts", because it removes the conflict at its cause: a script is atomic on the
server, so it needs no `WATCH` and cannot be aborted by a write to another field, and its condition can look at the
fields that matter (the lease holder and token, a record's token) instead of whole values. It keeps the stored
layout and the keys, so 5.x and 6.0 nodes can share a group: a script's write changes the key as before, so a 5.x
node's `WATCH` still aborts correctly.

The strongest argument against A: the per-group ACL user needs scripting commands (`EVALSHA`, `EVAL`, `SCRIPT LOAD`;
in practice `+@scripting`), which widens what a node's credentials allow, and some managed Redis offerings restrict
scripting (unverified – please confirm for the services hosts use). A script runs only commands the user may run, on keys it may access (Redis 7 checks each call against the
ACL; unverified for Valkey 8.1 – please confirm, and the tests must show it), so the widening is to running Lua over the group's own keys. Given that, A is proposed for 6.0, where operators
expect to change their setup (ADR-0034), with a TM-0003 revision before implementation.

Implementation outline (to be confirmed in review):

* One script per write, encoding and decoding the stored JSON (ADR-0017's format, Newtonsoft on the .NET side,
  `cjson` in the script); the .NET side keeps the decisions it can make before the write and moves only the condition
  and the write into the script. Date and number formats are checked so that 5.x nodes read what 6.0 writes.
* The ADR-0026 lease guard and the fencing checks move into the scripts unchanged in meaning;
  `docs/tlaplus/LeaderGlobalBarrier` keeps modelling the writes as atomic per record (ADR-0036 Note: the
  coordinator's own record first).
* Before implementation: TM-0003 revision (the scripting commands in the minimal ACL, the risk of a compromised
  node running Lua over its keys) and the operator steps in the 6.0 migration guide.
* Release in 6.0.0 with its change record (CR-0016, with ADR-0034's 6.0 step).

### Consequences

* Good, because a write no longer fails because another node wrote another field: PRB-0009's cause is gone, not
  narrowed, and each write is one round trip instead of three to five.
* Good, because the stored layout, the keys and the clock (`TIME`) stay as they are; groups can mix 5.x and 6.0
  nodes during an upgrade.
* Bad, because operators must add scripting to the per-group ACL user before upgrading to 6.0, and hosts on a Redis
  service that forbids scripting cannot upgrade.
* Bad, because the conditions are now Lua, tested only against real servers (Redis 7.0 and Valkey 8.1), and JSON is
  encoded by two libraries.

### Confirmation

* `RedisNetworkFaultTests`, seed `1196549739`, alone on Redis 7.0 and Valkey: 10 runs without a failure; the full
  Redis suite on both servers, including the per-group ACL user tests with the new command set.
* A mixed group (5.2 and 6.0 nodes) assigns and stops correctly (a rehearsal recorded in CR-0016).
* TM-0003 revision accepted before implementation.

## Sources

- `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md` (Decision Outcome: compare-and-set transactions, `TIME`)
- `docs/adrs/0020-per-group-redis-leases-and-resources.md` (the per-group keys and stored layout)
- `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md` (the lease guard)
- `docs/adrs/0034-public-api-of-the-lease-based-backends-in-6-0.md` (6.0 as the release that changes setup)
- `docs/adrs/0036-redis-coordinator-writes-client-records-one-at-a-time.md` (per-record writes; Note: own record first)
- `docs/threat-models/TM-0003-redis-coordination.md` (controls: the minimal ACL command set)
- `docs/problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md` (Root cause analysis)
