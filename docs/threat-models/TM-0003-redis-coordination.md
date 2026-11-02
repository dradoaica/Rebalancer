---
id: TM-0003
status: Accepted
date: 2026-11-02
scope: "Rebalancer.Redis provider and the Redis keys it uses"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
depends-on: [ "ADR-0002", "ADR-0008", "ADR-0014", "ADR-0015", "ADR-0016", "ADR-0017", "US-0006", "CR-0001", "CR-0003", "ADR-0022", "ADR-0023", "CR-0007", "ADR-0020", "ADR-0029", "ADR-0031", "CR-0004", "CR-0006", "CR-0013", "ADR-0036", "ADR-0037", "PRB-0009" ]
supersedes: [ ]
superseded-by: ""
---

# Threat model: Redis coordination backend

## Summary

Coordination state lives in these Redis keys:

- `Dictionary:RBR:ResourceGroups` (hash, shared by all groups): leases.
- `RBR:Clients:{group}` (hash, one per group, since 3.0; ADR-0015): client registrations with their assignments. The
  group name is percent-encoded outside `A-Z a-z 0-9 - _ . ~`.
- `Dictionary:RBR:Clients` (hash, shared): the 2.x client hash. In 3.0 the coordinator moves its group's entries out of
  it, once per lease tenure; afterwards it holds only groups not yet upgraded.
- `List:RBR:Resources` (list, shared by all groups): resources.

Values are Newtonsoft JSON. Lease and client updates are compare-and-set transactions evaluated on Redis server time (ADR-0017, formerly ADR 0006); since 6.0 they are server-side scripts (revision 7). The library has no access control of its own: it relies on the Redis connection (password or ACL user, TLS). Anyone who can write these keys controls the assignments of the groups they cover.

**Revision 2 (3.0.0, CR-0003):**

- Client records are per group (ADR-0015), and stale ones expire under the coordinator's fencing token (ADR-0014).
  Heartbeat reads no longer grow with other groups or with past restarts (D1 resolved).
- A Redis ACL user can now be limited to one group's client hash. Leases and resources stay shared.

**Revision 3 (3.1.0, CR-0004):** the minimal ACL command set is verified, and an end-to-end test covers it. Leases
and resources per group are decided in ADR-0020 (Accepted; not implemented yet). [2026-10-19: implemented in 4.0.0,
see revision 4.]

**Revision 4 (4.0.0, CR-0006; accepted by dradoaica, 2026-10-19):**

- Leases and settings move to `RBR:Lease:{group}` (hash, one field) and resources to `RBR:Resources:{group}` (list),
  ADR-0020. A moved group touches no shared key, so a Redis ACL user can be confined to its group completely (`~RBR:Lease:{g} ~RBR:Resources:{g} ~RBR:Clients:{g}`), verified end to end by `RedisClientRecordTests`.
- Heartbeats read only the group's own resource list (residual risk "resources list read in full" resolved).
- New, transitional: the first 4.0 node of a group moves its entries out of the shared keys, so it needs read and write
  access to them once. The move is one transaction guarded by the per-group key's absence and the shared entries'
  values, so a concurrent writer makes it retry instead of losing or duplicating entries (`RedisPerGroupKeysTests`).
  Mitigation: run the first start with a temporary user that has shared-key access, or provision the per-group keys as
  an operator (`docs/release-notes/migrating-to-4.0.md`).

**Revision 5 (4.1.0, CR-0007; accepted by dradoaica, 2026-10-19):** telemetry and health checks (ADR-0022, ADR-0023).

- **New data flow out of the process:** metrics and traces carry `rebalancer.backend`, `rebalancer.group` and, on
  the rebalance activity, `rebalancer.fencing_token`. Resource names, node identifiers, connection strings and credentials are never
  emitted (verified by the Redis telemetry tests, which fail on a resource name in any tag). Threat: whoever reads
  the telemetry backend learns group names, how often groups rebalance and coordinators change. Risk: **Low**; group
  names are deployment-chosen labels. Mitigation: treat the telemetry backend as Internal data; drop or hash the group
  tag in the collector where group names are sensitive.
- **Health check output** carries only a state and a short reason (no resource names or connection details); it reads
  in-process state and never calls Redis.

**Revision 6 (5.0.0, CR-0013; accepted by dradoaica, 2026-11-01):** carries revision 4 (per-group keys, 4.0) into
the body, and records 5.0.

- **Body brought up to 4.0:** the trust boundary between groups, the assets, the data flows and the ACL control now
  name the per-group keys `RBR:Lease:{group}`, `RBR:Resources:{group}` and `RBR:Clients:{group}`. The shared 3.x keys
  `Dictionary:RBR:ResourceGroups` and `List:RBR:Resources` remain only as the layout a 4.0+ node moves a group out of.
- **Scope:** the coordinator, follower, store and provider loop moved, unchanged, to `src/Rebalancer.LeaseCoordination`
  (ADR-0029, superseding ADR-0002); they are in scope here for Redis. Threats and controls are unchanged by the move.
- **Valkey (ADR-0031):** every Redis integration test, including the minimal per-group ACL user, passes on Valkey
  8.1.11; this model applies to Valkey unchanged.

**Revision 7 (6.0.0, CR-0016; accepted by dradoaica, 2026-11-02):** guarded writes become server-side Lua scripts (ADR-0037).

- **What changes:** keep-alives, lease acquire / renew / relinquish, the coordinator's guarded client writes and the
  expiry deletion run as scripts (`EVALSHA`, falling back to `EVAL` when the server has not cached the script). Each
  checks its condition (lease holder and fencing token, a record's token and existence) and writes in one atomic
  step on the server. No `WATCH`, no retries. Keys, layout and JSON format are unchanged.
- **Per-group ACL user:** needs scripting commands in addition to revision 4's set, in practice `+@scripting` (the
  minimal set is established by the ACL tests during implementation). Verified 2026-11-02 (CR-0016): `+eval
  +evalsha` suffice on Redis 7.0 and Valkey 8.1. Redis 7 checks each command a script runs
  against the caller's ACL, so a script can only touch the keys the user may access (unverified for Valkey 8.1 – the
  tests must show it; shown 2026-11-02 on Redis 7.0 and Valkey 8.1, for declared and undeclared keys:
  `RedisClientRecordTests.AScriptOfThePerGroupUser_CannotReachAnotherGroupsKeys`). Operators add the commands before
  upgrading a group to 6.0.
- **D2 corrected:** PRB-0009 showed that in 5.x contention is not Low over slow links: `WATCH` covers whole hashes, so
  every node's keep-alive aborts the others' writes, and retries run out (rated Medium below for 5.x). In 6.0 scripts
  are not aborted by other writes (Low).
- **New D5:** scripting lets anyone holding a node's credentials run arbitrary Lua over that group's keys, including
  scripts that run long and block the whole Redis instance (scripts run one at a time). See Denial of Service.
- Threats T1, T2 (tampering) and elevation of privilege are unchanged in kind: a script reaches the same keys the
  user could already write.
- **As implemented (CR-0016, 2026-11-02), correcting the first bullet:** the client status and the reset are scripts
  too. The lease writes still retry, but only when the lease field itself changed, as before; the two one-off
  migrations, of 2.x client records (ADR-0015) and of 3.x shared keys (ADR-0020), keep their transactions on shared
  keys, which a per-group user cannot reach anyway, so `+@transaction` stays. The
  client library sends `EVAL` once and `EVALSHA` afterwards, and tries `SCRIPT LOAD`, which the per-group user is
  denied; that is not needed (`EVAL` caches the script on the server) and shows in `ACL LOG` like the handshake
  commands.

## System context and boundaries

- **In scope:** `RedisProvider`, `LeaseService`, `ClientService`, `ResourceService`, `RedisHash`, `RedisScripts` (the
  Lua scripts, revision 7), `PerGroupKeyMigration`; the shared coordination in `Rebalancer.LeaseCoordination` as used by Redis (revision 6); the
  per-group keys `RBR:Lease:{group}`, `RBR:Resources:{group}`, `RBR:Clients:{group}`; the `TIME` command used for
  server time.
- **Out of scope:** Redis server hardening (persistence, replication, `rename-command`, protected mode); Redis Cluster
  slot placement (US-0006 non-goal).
- **Assumptions:**
  - The connection string is supplied by the host application and is a secret.
  - The operator seeds `ResourceGroups` and `Resources`.
  - All nodes of a group run the same version: a mixed group would mix client and server clocks (CR-0001).
- **Trust boundaries:**
  1. Application process ↔ Redis (network, RESP).
  2. Rebalancer nodes ↔ other Redis clients and other applications using the same Redis database.
  3. Resource group ↔ resource group: since 4.0 every key belongs to one group, so an ACL user per group confines a
     node to its own group (ADR-0020); without per-group users, one Redis database is one trust domain.

## Asset and data classification

| Asset / Data                                                  | Classification | Notes                                                 |
|---------------------------------------------------------------|----------------|-------------------------------------------------------|
| `RBR:Clients:{group}` (assignments, statuses, fencing tokens) | Confidential   | Integrity-critical; per group since 3.0               |
| `Dictionary:RBR:Clients` (2.x client hash)                    | Confidential   | Transitional: groups not yet upgraded to 3.0          |
| `RBR:Lease:{group}` (lease, group settings)                   | Confidential   | Integrity-critical: leader election and fencing       |
| `RBR:Resources:{group}` (resource names)                      | Internal       | Integrity-critical: defines the work                  |
| `Dictionary:RBR:ResourceGroups`, `List:RBR:Resources`         | Confidential   | 3.x layout; moved out of on a group's first 4.0 start |
| Connection string (password / ACL user, TLS settings)         | Restricted     | Grants the rights above                               |
| `CoordinatorServer` (`Environment.MachineName`)               | Internal       | Reveals host names                                    |

## Data flows

1. Node → RESP → Redis – one `ConnectionMultiplexer` per provider, closed on dispose (ADR-0016, formerly ADR 0006).
2. Node → `HGET` / `TIME` / transaction with a hash-equality condition + `HSET` on `RBR:Lease:{group}` – acquire, renew
   or relinquish the lease. Since 6.0 the condition and `HSET` are one script comparing the lease field (revision 7).
3. Node → `HSET` / compare-and-set on `RBR:Clients:{group}` – register, keep-alive, status changes; `HDEL` of its own
   entry on a clean stop (since 3.0). Since 6.0 keep-alive, status and reset are scripts that change only their fields.
4. Coordinator → one transaction on `Clients` with a condition per client – stop or grant resources, guarded by
   fencing tokens. Since 5.2 one write per client in order (ADR-0036); since 6.0 each is a script checking the lease
   holder and token and the record's token (revision 7).
5. Node → `HVALS` on `RBR:Clients:{group}`, `LRANGE` on `RBR:Resources:{group}` – read live clients and resources (every
   heartbeat).
6. Coordinator → `HGETALL` + transaction with `HDEL` on `RBR:Clients:{group}` (since 3.0) – expire entries older than
   the retention by Redis `TIME`, conditioned on its unchanged lease record (ADR-0014). Since 6.0 a script deletes the
   entries still unchanged while the lease names this coordinator and token.
7. Coordinator → `HGETALL` on `Dictionary:RBR:Clients` + transaction moving its group's entries (since 3.0, once per
   lease tenure, conditioned on its lease; skipped if not permitted) (ADR-0015).
8. Operator / tools → write `ResourceGroups`, `Resources` – provisioning.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a client with write access adds a fake client entry or takes the lease with a crafted `CoordinatorId`.
- Affected flow/asset: flows 2, 3.
- Mitigation (s): Redis authentication. Use a dedicated ACL user limited to the `RBR` keys (see Controls mapping).
  Without a password, anyone who reaches the port can write.
- Risk: **High** without authentication; **Medium** with a shared password; **Low to Medium** with a dedicated ACL user.

### Tampering

- Threat T1: rewrite assignments or leases in the shared hashes.
- Mitigation (s): authentication and key-scoped ACLs. Compare-and-set protects against concurrent nodes, not against a
  malicious writer.
- Risk: **Medium** with authentication; **High** without.
- Threat T2: malformed JSON written into a hash breaks every node that reads it. Deserialization throws, the role task
  faults, and nodes keep restarting.
- Mitigation (s): same as T1. Newtonsoft is used with default settings, so no type-name handling is enabled and
  arbitrary types cannot be instantiated from the data.
- Risk: **Medium** (availability impact through integrity).

### Repudiation

- Threat: changes cannot be attributed.
- Mitigation (s): none in the library; Redis ACL logs (`ACL LOG`) record only denials. Shared users make nodes
  indistinguishable.
- Risk: **Low to Medium**.

### Information Disclosure

- Threat I1: the password in the connection string leaks through configuration or logs.
- Mitigation (s): the library does not log connection strings (checked 2026-10-18); secret store.
- Risk: **Medium**.
- Threat I2: traffic, including the password at `AUTH`, is read on the network.
- Mitigation (s): `ssl=true` in the connection string with certificate validation (US-0006 lists TLS as a deployment
  concern).
- Risk: **Medium** without TLS.

### Denial of Service

- Threat D1: client entries were never deleted, and every node read every group's entries on every heartbeat, so cost
  grew with every restart, across all groups.
- Mitigation (s): since 3.0, entries live per group (ADR-0015), stale entries expire under the coordinator's fencing
  token, and a clean stop deletes the node's own entry (ADR-0014).
- Risk: **Low** (revision 1: Medium).
- Threat D2: high contention on the shared hashes makes compare-and-set retries run out (10 attempts), and the lease
  call reports an error.
- Mitigation (s): server-side contention is limited to one lease field per group; the retry limit turns livelock into
  a visible error. Revision 7: in 5.x, `WATCH` covers the whole clients hash and lease hash, so over a link whose
  round trip is a sizeable fraction of the heartbeat the retries run out repeatedly and a group may not settle (PRB-0009; ADR-0036 in 5.2.0 reduced it). In 6.0 the writes are scripts, which other writes cannot abort (ADR-0037).
- Risk: **Medium** for 5.x over slow links (revision 7; was rated Low); **Low** in 6.0.
- Threat D3: `FLUSHDB`, `DEL` or eviction (`maxmemory` policy) removes coordination state.
- Mitigation (s): ACL user without `@dangerous` / `@keyspace` delete commands; a `noeviction` policy (or no eviction of
  these keys) on the database used.
- Risk: **Medium** on shared or cache-configured instances.
- Threat D4 (revision 2): a writer deletes live entries from `RBR:Clients:{group}`. Each affected node stops its
  activity and registers again (ADR-0014); repeated deletes keep the group rebalancing.
- Mitigation (s): ACL user per group (or per trust domain); safety holds, because a node whose entry disappears stops
  before it registers again.
- Risk: **Low** with per-group ACL users; **Medium** with a shared user.
- Threat D5 (revision 7, 6.0): a holder of a node's credentials runs a script that loops or does heavy work. Redis
  runs scripts one at a time, so the instance stops serving every client, of every group and application, until the
  script ends or is killed; after `busy-reply-threshold` (default 5 s) other clients get `BUSY` errors.
- Mitigation (s): per-group credentials kept secret as before; an operator can end a read-only script with
  `SCRIPT KILL` (a script that already wrote can only be stopped with `SHUTDOWN NOSAVE`); a dedicated Redis instance
  for Rebalancer limits the blast radius to Rebalancer groups. Granting only `EVALSHA` and loading the scripts with an
  admin user would prevent arbitrary scripts, at the cost of reloading them after every server restart; not chosen as
  the default (to reconsider if the risk is not accepted).
- Risk: **Medium** on an instance shared with other applications; **Low** on a dedicated instance.

### Elevation of Privilege

- Threat: credentials for group A can modify group B.
- Mitigation (s): since 3.0, client records can be separated with key-scoped ACL users (`~RBR:Clients:{group}`).
  Leases (`Dictionary:RBR:ResourceGroups`) and resources (`List:RBR:Resources`) are still shared, so a group's user
  can still change another group's lease or resource list. Use separate Redis databases or instances for full
  isolation.
- Risk: **Medium** where groups of different trust share a Redis database (lower than revision 1 for client records); **Low** otherwise.

## Controls mapping

| Threat / Risk                          | Control (design or runtime)                                                                                                                                                                                                                                                                                                                                                                                                                        | Evidence / Reference                                                                                                                                                                |
|----------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Spoofing / tampering                   | Dedicated ACL user per group (or trust domain) with key patterns `~RBR:Lease:{group} ~RBR:Resources:{group} ~RBR:Clients:{group}` (4.0; revision 6). Minimal command set, verified 2026-10-18 (redis:7.0, StackExchange.Redis 3.4.5): `+@read +@write +@transaction +time +echo` with `resetchannels`; `ECHO` is required by the connection handshake, the library's other handshake commands are denied but tolerated (they appear in `ACL LOG`). | `tests/Rebalancer.IntegrationTests/Redis/RedisClientRecordTests.cs` (`ARebalancerClient_WorksEndToEnd_WithTheMinimalPerGroupUser`, `ExpiryWorks_ForARedisAclUserLimitedToItsGroup`) |
| Spoofing / tampering (6.0, revision 7) | As above, plus the scripting commands ADR-0037 needs: `+eval +evalsha` (verified on Redis 7.0 and Valkey 8.1, CR-0016). Scripts run under the caller's ACL, so the key patterns still apply (to show in the tests, also for Valkey).                                                                                                                                                                                                               | `RedisClientRecordTests` (the per-group user tests, with the 6.0 command set)                                                                                                       |
| Credential and traffic disclosure      | Secret store; `ssl=true`; no connection strings in logs                                                                                                                                                                                                                                                                                                                                                                                            | US-0006 Security & compliance                                                                                                                                                       |
| Unsafe deserialization                 | Newtonsoft default settings (no `TypeNameHandling`)                                                                                                                                                                                                                                                                                                                                                                                                | `src/Rebalancer.Redis/Utils/RedisHash.cs`                                                                                                                                           |
| Clients hash growth (D1)               | Per-group hash; expiry under the fencing token; own-entry delete on clean stop                                                                                                                                                                                                                                                                                                                                                                     | ADR-0014, ADR-0015; `RedisClientRecordTests`                                                                                                                                        |
| State loss                             | `noeviction` (or equivalent) on the database used                                                                                                                                                                                                                                                                                                                                                                                                  | Deployment responsibility                                                                                                                                                           |
| Long-running scripts (D5)              | Per-group credentials; `busy-reply-threshold`; `SCRIPT KILL`; a dedicated instance for Rebalancer                                                                                                                                                                                                                                                                                                                                                  | Deployment responsibility; migration guide 6.0                                                                                                                                      |
| Mixed-version clocks                   | All-nodes-at-once upgrade of Redis groups                                                                                                                                                                                                                                                                                                                                                                                                          | CR-0001 (Implementation plan, step 3)                                                                                                                                               |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **Telemetry exports group names** (revision 5): accepted as Low; filter in the collector if needed.
  - **Partial per-group isolation** inside one Redis database: client records can be isolated (3.0), leases and
    resources cannot. Revision 4: resolved in 4.0 for groups that have moved (ADR-0020).
  - ~~**Unbounded growth of the clients hash**~~ Resolved in 3.0 (ADR-0014, ADR-0015).
  - **The resources list is still read in full** on every heartbeat (`LRANGE`); it grows with the resources of all
    groups, not with restarts. Revision 4: resolved in 4.0 (one list per group).
  - **No audit trail.**
  - **Scripting widens what a node's credentials allow** (revision 7, 6.0): arbitrary Lua over the group's keys, and
    instance-wide blocking by a long script (D5). Accepted by dradoaica as Medium on shared instances, Low on a
    dedicated instance (2026-11-02).
- Follow-ups of revision 1, both decided and implemented in 3.0.0: expiry of client entries (ADR-0014); one client hash
  per group with a migration (ADR-0015). Per-group keys for leases and resources: ADR-0020 (Accepted; implemented in 4.0.0, CR-0006).
- Decision: revision 1 accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on
  2026-10-18; revision 2 accepted by dradoaica on 2026-10-18. Revision 3 accepted by dradoaica (in session
  2026-10-19; also recorded in commit `acce411` of 2026-10-18).

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0029-…` (superseding ADR-0002), `docs/adrs/0008-…`, `docs/adrs/0014-…`,
  `docs/adrs/0015-…`, `docs/adrs/0016-…`, `docs/adrs/0017-…`, `docs/adrs/0020-…`, `docs/adrs/0031-…` (all Accepted;
  ADR 0006 is superseded by ADR-0016 and ADR-0017 for the parts used here).
- Required SDPs: N/A (library).
- Change records to implement mitigations: `docs/change-records/CR-0003-release-3-0-0.md`.
- Runbooks / monitors to add: none for ACLs (the verified per-group user definition is in
  `docs/release-notes/migrating-to-3.0.md`); the clients-hash cleanup procedure is no longer needed.

## Approvals

| Role                                 | Name      | Date       |
|--------------------------------------|-----------|------------|
| Service owner (revision 1)           | dradoaica | 2026-10-18 |
| Security / architecture (revision 1) | dradoaica | 2026-10-18 |
| Service owner (revision 2)           | dradoaica | 2026-10-18 |
| Security / architecture (revision 2) | dradoaica | 2026-10-18 |
| Service owner (revision 3)           | dradoaica | 2026-10-19 |
| Security / architecture (revision 3) | dradoaica | 2026-10-19 |
| Service owner (revision 4)           | dradoaica | 2026-10-19 |
| Security / architecture (revision 4) | dradoaica | 2026-10-19 |
| Service owner (revision 5)           | dradoaica | 2026-10-19 |
| Security / architecture (revision 5) | dradoaica | 2026-10-19 |
| Service owner (revision 6)           | dradoaica | 2026-11-01 |
| Security / architecture (revision 6) | dradoaica | 2026-11-01 |
| Service owner (revision 7)           | dradoaica | 2026-11-02 |
| Security / architecture (revision 7) | dradoaica | 2026-11-02 |

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0014-expire-stale-client-records.md`,
  `docs/adrs/0015-store-redis-clients-per-resource-group.md`, `docs/adrs/0016-rebalancer-client-owns-its-provider.md`,
  `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md`,
  `docs/adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md`,
  `docs/adrs/0020-per-group-redis-leases-and-resources.md`,
  `docs/adrs/0029-shared-coordination-project-for-sqlserver-and-redis.md` (revision 6),
  `docs/adrs/0031-valkey-as-a-supported-server-for-the-redis-backend.md` (revision 6),
  `docs/adrs/0036-redis-coordinator-writes-client-records-one-at-a-time.md`,
  `docs/adrs/0037-redis-guarded-writes-as-server-side-scripts.md` (revision 7)
- Problem records: `docs/problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md` (revision 7: D2)
- Related user stories: `docs/user-stories/US-0006-coordinate-via-redis-backend.md`
- Change records: `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md`,
  `docs/change-records/CR-0003-release-3-0-0.md`, `docs/change-records/CR-0004-release-3-1-0.md`,
  `docs/change-records/CR-0006-release-4-0-0.md`, `docs/change-records/CR-0013-release-5-0-0.md` (revision 6)
- Code: `src/Rebalancer.Redis/`, `src/Rebalancer.LeaseCoordination/` (revision 6)
- External standards: STRIDE; OWASP ASVS V4 (access control), V8 (data protection) – control IDs not mapped (not required: decided by dradoaica 2026-10-19; chapter-level references suffice for a library)
