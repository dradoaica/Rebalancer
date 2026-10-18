# Threat model: Redis coordination backend

## Metadata

| Field            | Value                                                                       |
|------------------|-----------------------------------------------------------------------------|
| **ID**           | TM-0003                                                                     |
| **Status**       | Accepted                                                                    |
| **Scope**        | `Rebalancer.Redis` provider and the Redis keys it uses                      |
| **Owners**       | Maintainer (dradoaica)                                                      |
| **Last updated** | 2026-10-18                                                                  |

## Summary

All coordination state lives in three Redis keys shared by every resource group:
- `Dictionary:RBR:ResourceGroups` (hash): leases.
- `Dictionary:RBR:Clients` (hash): client registrations with their assignments.
- `List:RBR:Resources` (list): resources.

Values are Newtonsoft JSON. Lease and client updates are compare-and-set transactions evaluated on Redis server time
(ADR 0006). The library has no access control of its own: it relies on the Redis connection (password or ACL user,
TLS). Anyone who can write the three keys controls the assignments of every group. Two more risks: every node reads the
whole clients hash on each heartbeat, and that hash grows without bound.

## System context and boundaries

- **In scope:** `RedisProvider`, `LeaseService`, `ClientService`, `ResourceService`, `RedisHash`; the three keys above;
  the `TIME` command used for server time.
- **Out of scope:** Redis server hardening (persistence, replication, `rename-command`, protected mode); Redis Cluster
  slot placement (US-006 non-goal).
- **Assumptions:**
  - The connection string is supplied by the host application and is a secret.
  - The operator seeds `ResourceGroups` and `Resources`.
  - All nodes of a group run the same version: a mixed group would mix client and server clocks (CR-2026-10-18).
- **Trust boundaries:**
  1. Application process ↔ Redis (network, RESP).
  2. Rebalancer nodes ↔ other Redis clients and other applications using the same Redis database.
  3. Resource group ↔ resource group: no boundary, as all groups share the same three keys.

## Asset and data classification

| Asset / Data                                                    | Classification | Notes                                                              |
|-----------------------------------------------------------------|----------------|--------------------------------------------------------------------|
| `Dictionary:RBR:Clients` (assignments, statuses, fencing tokens)  | Confidential   | Integrity-critical                                                 |
| `Dictionary:RBR:ResourceGroups` (leases)                          | Confidential   | Integrity-critical: leader election and fencing                    |
| `List:RBR:Resources` (resource names)                             | Internal       | Integrity-critical: defines the work                               |
| Connection string (password / ACL user, TLS settings)             | Restricted     | Grants the rights above                                            |
| `CoordinatorServer` (`Environment.MachineName`)                   | Internal       | Reveals host names                                                 |

## Data flows

1. Node → RESP → Redis – one `ConnectionMultiplexer` per provider, closed on dispose (ADR 0006).
2. Node → `HGET` / `TIME` / transaction with a hash-equality condition + `HSET` on `ResourceGroups` – acquire, renew
   or relinquish the lease.
3. Node → `HSET` / compare-and-set on `Clients` – register, keep-alive, status changes.
4. Coordinator → one transaction on `Clients` with a condition per client – stop or grant resources, guarded by
   fencing tokens.
5. Node → `HVALS` on `Clients`, `LRANGE` on `Resources` – read live clients and resources (every heartbeat).
6. Operator / tools → write `ResourceGroups`, `Resources` – provisioning.

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

- Threat D1: entries in `Dictionary:RBR:Clients` are never deleted. Each process start adds one, and every node reads
  the whole hash (`HVALS`) on every heartbeat, so cost grows with every restart, across all groups.
- Mitigation(s): none in the library today; operator cleanup of `Terminated` entries.
- Risk: **Medium** (slow, cumulative degradation of every group).
- Threat D2: high contention on the shared hashes makes compare-and-set retries run out (10 attempts), and the lease
  call reports an error.
- Mitigation(s): server-side contention is limited to one lease field per group; the retry limit turns livelock into
  a visible error.
- Risk: **Low**.
- Threat D3: `FLUSHDB`, `DEL` or eviction (`maxmemory` policy) removes coordination state.
- Mitigation(s): ACL user without `@dangerous` / `@keyspace` delete commands; a `noeviction` policy (or no eviction of
  these keys) on the database used.
- Risk: **Medium** on shared or cache-configured instances.

### Elevation of Privilege

- Threat: all groups share the same keys, so credentials for group A can modify group B.
- Mitigation(s): separate Redis databases or instances per trust domain. Key-scoped ACLs cannot separate groups,
  because they share keys.
- Risk: **Medium** where groups of different trust share a Redis database; **Low** otherwise.

## Controls mapping

| Threat / Risk                      | Control (design or runtime)                                                                                       | Evidence / Reference                                   |
|------------------------------------|-------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------|
| Spoofing / tampering               | Dedicated ACL user, e.g. `~Dictionary:RBR:* ~List:RBR:* +@read +@write +@transaction +time` (exact command set unverified – please confirm) | Commands used in `RedisHash`, services                  |
| Credential and traffic disclosure  | Secret store; `ssl=true`; no connection strings in logs                                                            | US-006 Security & compliance                           |
| Unsafe deserialization             | Newtonsoft default settings (no `TypeNameHandling`)                                                               | `src/Rebalancer.Redis/Utils/RedisHash.cs`              |
| Clients hash growth                | Operator cleanup of `Terminated` entries                                                                          | Not provided; follow-up                                |
| State loss                         | `noeviction` (or equivalent) on the database used                                                                 | Deployment responsibility                              |
| Mixed-version clocks               | All-nodes-at-once upgrade of Redis groups                                                                         | CR-2026-10-18 (Implementation plan, step 3)            |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **No per-group isolation** inside one Redis database.
  - **Unbounded growth of the clients hash,** read in full on every heartbeat.
  - **No audit trail.**
- Proposed follow-ups (each needs a decision record before implementation): remove or expire terminated client entries
  in the library; store clients per group (one hash per group) so reads scale with the group, not the whole
  installation. That is a data-format change, so it needs an ADR and a migration.
- Decision: Accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on 2026-10-18,
  including the residual risks above.

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0002-…` (Accepted), `docs/adrs/0006-…` (Accepted), `docs/adrs/0008-…` (Accepted).
- Required SDPs: N/A (library).
- Change records to implement mitigations: none yet.
- Runbooks / monitors to add: Redis ACL user definition; clients-hash cleanup procedure.

## Approvals

| Role                    | Name | Date |
|-------------------------|------|------|
| Service owner           | dradoaica | 2026-10-18 |
| Security / architecture | dradoaica | 2026-10-18 |

## Sources

- ADRs: `docs/adrs/0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md`,
  `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md`,
  `docs/adrs/0008-replace-redis-datatypes-with-direct-newtonsoft-json.md`
- Related user stories: `docs/user-stories/US-006-coordinate-via-redis-backend.md`
- Change records: `docs/change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md`
- Code: `src/Rebalancer.Redis/`
- External standards: STRIDE; OWASP ASVS V4 (access control), V8 (data protection) – control IDs not mapped
  (unverified – please confirm if required)
