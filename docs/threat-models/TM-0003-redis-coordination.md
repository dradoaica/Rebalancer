---
id: TM-0003
status: Accepted
date: 2026-10-18
scope: "Rebalancer.Redis provider and the Redis keys it uses"
owner: "dradoaica"
approved-by: [ "dradoaica" ]
depends-on: [ "ADR-0002", "ADR-0008", "ADR-0014", "ADR-0015", "ADR-0016", "ADR-0017", "US-006", "CR-0001", "CR-0003" ]
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

Values are Newtonsoft JSON. Lease and client updates are compare-and-set transactions evaluated on Redis server time
(ADR-0017, formerly ADR 0006). The library has no access control of its own: it relies on the Redis connection
(password or ACL user, TLS). Anyone who can write these keys controls the assignments of the groups they cover.

**Revision 2 (3.0.0, CR-0003):**
- Client records are per group (ADR-0015), and stale ones expire under the coordinator's fencing token (ADR-0014).
  Heartbeat reads no longer grow with other groups or with past restarts (D1 resolved).
- A Redis ACL user can now be limited to one group's client hash. Leases and resources stay shared.

**Revision 3 (3.1.0, CR-0004):** the minimal ACL command set is verified, and an end-to-end test covers it. Leases
and resources per group are decided in ADR-0020 (Accepted; not implemented yet).

## System context and boundaries

- **In scope:** `RedisProvider`, `LeaseService`, `ClientService`, `ResourceService`, `RedisHash`; the three keys above;
  the `TIME` command used for server time.
- **Out of scope:** Redis server hardening (persistence, replication, `rename-command`, protected mode); Redis Cluster
  slot placement (US-006 non-goal).
- **Assumptions:**
  - The connection string is supplied by the host application and is a secret.
  - The operator seeds `ResourceGroups` and `Resources`.
  - All nodes of a group run the same version: a mixed group would mix client and server clocks (CR-0001).
- **Trust boundaries:**
  1. Application process ↔ Redis (network, RESP).
  2. Rebalancer nodes ↔ other Redis clients and other applications using the same Redis database.
  3. Resource group ↔ resource group: no boundary, as all groups share the same three keys.

## Asset and data classification

| Asset / Data                                                    | Classification | Notes                                                              |
|-----------------------------------------------------------------|----------------|--------------------------------------------------------------------|
| `RBR:Clients:{group}` (assignments, statuses, fencing tokens)     | Confidential   | Integrity-critical; per group since 3.0                            |
| `Dictionary:RBR:Clients` (2.x client hash)                        | Confidential   | Transitional: groups not yet upgraded to 3.0                       |
| `Dictionary:RBR:ResourceGroups` (leases)                          | Confidential   | Integrity-critical: leader election and fencing                    |
| `List:RBR:Resources` (resource names)                             | Internal       | Integrity-critical: defines the work                               |
| Connection string (password / ACL user, TLS settings)             | Restricted     | Grants the rights above                                            |
| `CoordinatorServer` (`Environment.MachineName`)                   | Internal       | Reveals host names                                                 |

## Data flows

1. Node → RESP → Redis – one `ConnectionMultiplexer` per provider, closed on dispose (ADR-0016, formerly ADR 0006).
2. Node → `HGET` / `TIME` / transaction with a hash-equality condition + `HSET` on `ResourceGroups` – acquire, renew
   or relinquish the lease.
3. Node → `HSET` / compare-and-set on `RBR:Clients:{group}` – register, keep-alive, status changes; `HDEL` of its own
   entry on a clean stop (since 3.0).
4. Coordinator → one transaction on `Clients` with a condition per client – stop or grant resources, guarded by
   fencing tokens.
5. Node → `HVALS` on `RBR:Clients:{group}`, `LRANGE` on `Resources` – read live clients and resources (every
   heartbeat).
6. Coordinator → `HGETALL` + transaction with `HDEL` on `RBR:Clients:{group}` (since 3.0) – expire entries older than
   the retention by Redis `TIME`, conditioned on its unchanged lease record (ADR-0014).
7. Coordinator → `HGETALL` on `Dictionary:RBR:Clients` + transaction moving its group's entries (since 3.0, once per
   lease tenure, conditioned on its lease; skipped if not permitted) (ADR-0015).
8. Operator / tools → write `ResourceGroups`, `Resources` – provisioning.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a client with write access adds a fake client entry or takes the lease with a crafted `CoordinatorId`.
- Affected flow/asset: flows 2, 3.
- Mitigation(s): Redis authentication. Use a dedicated ACL user limited to the `RBR` keys (see Controls mapping).
  Without a password, anyone who reaches the port can write.
- Risk: **High** without authentication; **Medium** with a shared password; **Low to Medium** with a dedicated ACL user.

### Tampering

- Threat T1: rewrite assignments or leases in the shared hashes.
- Mitigation(s): authentication and key-scoped ACLs. Compare-and-set protects against concurrent nodes, not against a
  malicious writer.
- Risk: **Medium** with authentication; **High** without.
- Threat T2: malformed JSON written into a hash breaks every node that reads it. Deserialization throws, the role task
  faults, and nodes keep restarting.
- Mitigation(s): same as T1. Newtonsoft is used with default settings, so no type-name handling is enabled and
  arbitrary types cannot be instantiated from the data.
- Risk: **Medium** (availability impact through integrity).

### Repudiation

- Threat: changes cannot be attributed.
- Mitigation(s): none in the library; Redis ACL logs (`ACL LOG`) record only denials. Shared users make nodes
  indistinguishable.
- Risk: **Low to Medium**.

### Information Disclosure

- Threat I1: the password in the connection string leaks through configuration or logs.
- Mitigation(s): the library does not log connection strings (checked 2026-10-18); secret store.
- Risk: **Medium**.
- Threat I2: traffic, including the password at `AUTH`, is read on the network.
- Mitigation(s): `ssl=true` in the connection string with certificate validation (US-006 lists TLS as a deployment
  concern).
- Risk: **Medium** without TLS.

### Denial of Service

- Threat D1: client entries were never deleted, and every node read every group's entries on every heartbeat, so cost
  grew with every restart, across all groups.
- Mitigation(s): since 3.0, entries live per group (ADR-0015), stale entries expire under the coordinator's fencing
  token, and a clean stop deletes the node's own entry (ADR-0014).
- Risk: **Low** (revision 1: Medium).
- Threat D2: high contention on the shared hashes makes compare-and-set retries run out (10 attempts), and the lease
  call reports an error.
- Mitigation(s): server-side contention is limited to one lease field per group; the retry limit turns livelock into
  a visible error.
- Risk: **Low**.
- Threat D3: `FLUSHDB`, `DEL` or eviction (`maxmemory` policy) removes coordination state.
- Mitigation(s): ACL user without `@dangerous` / `@keyspace` delete commands; a `noeviction` policy (or no eviction of
  these keys) on the database used.
- Risk: **Medium** on shared or cache-configured instances.
- Threat D4 (revision 2): a writer deletes live entries from `RBR:Clients:{group}`. Each affected node stops its
  activity and registers again (ADR-0014); repeated deletes keep the group rebalancing.
- Mitigation(s): ACL user per group (or per trust domain); safety holds, because a node whose entry disappears stops
  before it registers again.
- Risk: **Low** with per-group ACL users; **Medium** with a shared user.

### Elevation of Privilege

- Threat: credentials for group A can modify group B.
- Mitigation(s): since 3.0, client records can be separated with key-scoped ACL users (`~RBR:Clients:{group}`).
  Leases (`Dictionary:RBR:ResourceGroups`) and resources (`List:RBR:Resources`) are still shared, so a group's user
  can still change another group's lease or resource list. Use separate Redis databases or instances for full
  isolation.
- Risk: **Medium** where groups of different trust share a Redis database (lower than revision 1 for client records);
  **Low** otherwise.

## Controls mapping

| Threat / Risk                      | Control (design or runtime)                                                                                       | Evidence / Reference                                   |
|------------------------------------|-------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------|
| Spoofing / tampering               | Dedicated ACL user per group (or trust domain) with key patterns `~RBR:Clients:{group} ~Dictionary:RBR:ResourceGroups ~List:RBR:Resources`. Minimal command set, verified 2026-10-18 (redis:7.0, StackExchange.Redis 3.4.5): `+@read +@write +@transaction +time +echo` with `resetchannels`; `ECHO` is required by the connection handshake, the library's other handshake commands are denied but tolerated (they appear in `ACL LOG`). | `tests/Rebalancer.IntegrationTests/Redis/RedisClientRecordTests.cs` (`ARebalancerClient_WorksEndToEnd_WithTheMinimalPerGroupUser`, `ExpiryWorks_ForARedisAclUserLimitedToItsGroup`) |
| Credential and traffic disclosure  | Secret store; `ssl=true`; no connection strings in logs                                                            | US-006 Security & compliance                           |
| Unsafe deserialization             | Newtonsoft default settings (no `TypeNameHandling`)                                                               | `src/Rebalancer.Redis/Utils/RedisHash.cs`              |
| Clients hash growth (D1)           | Per-group hash; expiry under the fencing token; own-entry delete on clean stop                                   | ADR-0014, ADR-0015; `RedisClientRecordTests`           |
| State loss                         | `noeviction` (or equivalent) on the database used                                                                 | Deployment responsibility                              |
| Mixed-version clocks               | All-nodes-at-once upgrade of Redis groups                                                                         | CR-0001 (Implementation plan, step 3)            |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **Partial per-group isolation** inside one Redis database: client records can be isolated (3.0), leases and
    resources cannot.
  - ~~**Unbounded growth of the clients hash**~~ Resolved in 3.0 (ADR-0014, ADR-0015).
  - **The resources list is still read in full** on every heartbeat (`LRANGE`); it grows with the resources of all
    groups, not with restarts.
  - **No audit trail.**
- Follow-ups of revision 1, both decided and implemented in 3.0.0: expiry of client entries (ADR-0014); one client hash
  per group with a migration (ADR-0015). Per-group keys for leases and resources: ADR-0020 (Accepted; not
  implemented yet).
- Decision: revision 1 accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on
  2026-10-18; revision 2 accepted by dradoaica on 2026-10-18. Revision 3: {Accepted by <role> on <date>} – pending
  human review.

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0002-…`, `docs/adrs/0008-…`, `docs/adrs/0014-…`, `docs/adrs/0015-…`, `docs/adrs/0016-…`,
  `docs/adrs/0017-…` (all Accepted; ADR 0006 is superseded by ADR-0016 and ADR-0017 for the parts used here).
- Required SDPs: N/A (library).
- Change records to implement mitigations: `docs/change-records/CR-0003-release-3-0-0.md`.
- Runbooks / monitors to add: none for ACLs (the verified per-group user definition is in
  `docs/release-notes/migrating-to-3.0.md`); the clients-hash cleanup procedure is no longer needed.

## Approvals

| Role                    | Name | Date |
|-------------------------|------|------|
| Service owner (revision 1)           | dradoaica | 2026-10-18 |
| Security / architecture (revision 1) | dradoaica | 2026-10-18 |
| Service owner (revision 2)           | dradoaica | 2026-10-18 |
| Security / architecture (revision 2) | dradoaica | 2026-10-18 |
| Service owner (revision 3)           |           |            |
| Security / architecture (revision 3) |           |            |

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0014-expire-stale-client-records.md`,
  `docs/adrs/0015-store-redis-clients-per-resource-group.md`, `docs/adrs/0016-rebalancer-client-owns-its-provider.md`,
  `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md`,
  `docs/adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md`
- Related user stories: `docs/user-stories/US-006-coordinate-via-redis-backend.md`
- Change records: `docs/change-records/CR-0001-coordination-safety-and-provider-ownership.md`,
  `docs/change-records/CR-0003-release-3-0-0.md`
- Code: `src/Rebalancer.Redis/`
- External standards: STRIDE; OWASP ASVS V4 (access control), V8 (data protection) – control IDs not mapped
  (not required: decided by dradoaica 2026-10-19; chapter-level references suffice for a library)
