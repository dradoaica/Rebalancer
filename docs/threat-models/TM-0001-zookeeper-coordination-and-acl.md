# Threat model: ZooKeeper coordination backend and znode ACLs

## Metadata

| Field            | Value                                                                                       |
|------------------|---------------------------------------------------------------------------------------------|
| **ID**           | TM-0001                                                                                     |
| **Status**       | Accepted                                                                                    |
| **Scope**        | `Rebalancer.ZooKeeper` provider and its znodes, including the opt-in ACLs of ADR 0006        |
| **Owners**       | Maintainer (dradoaica)                                                                      |
| **Last updated** | 2026-10-18                                                                                  |

## Summary

Nodes of a resource group coordinate only through a ZooKeeper ensemble. Leader election, rebalancing status, barriers
and the assignment table all live in znodes. Whoever can write those znodes controls which node processes which
resource. Before ADR 0006 every znode was created with `OPEN_ACL_UNSAFE`. Network access to the ensemble was therefore
the only control, and any reachable client could change assignments, stop rebalancing, or impersonate a node.

ADR 0006 adds opt-in digest authentication and ACLs. They protect the znodes the provider creates, but not the znodes
the operator creates: the root, the group node, and the `resources` node with its children. The `resources` node
matters most, because its data holds the assignment table.

## System context and boundaries

- **In scope:** `ZooKeeperProvider`, `ZooKeeperService`, the znodes under `{root}/{group}`, the session credentials
  (`authScheme` / `authData`), and the DI registration (`AddRebalancerZooKeeper`).
- **Out of scope:** ZooKeeper server hardening (TLS, SASL/Kerberos, quorum authentication, OS hardening); the resources
  themselves (e.g., RabbitMQ queues); the SQL Server and Redis backends (no threat model yet).
- **Assumptions:**
  - All nodes of one resource group run the same Rebalancer version and use the same credentials.
  - The application hosting a node is trusted: it holds the credentials and runs the resource handlers.
  - The digest scheme is used as documented ("user:password"). Its credentials travel in clear text unless the
    client-to-server connection is encrypted.
- **Trust boundaries:**
  1. Application process ↔ ZooKeeper ensemble (network).
  2. Rebalancer nodes ↔ other ZooKeeper clients that share the ensemble (operators, tools, other applications).
  3. Operator provisioning (creating the group and resource znodes) ↔ runtime nodes.

## Asset and data classification

| Asset / Data                                                        | Classification | Notes                                                                                     |
|---------------------------------------------------------------------|----------------|-------------------------------------------------------------------------------------------|
| Assignment table (`{group}/resources` znode data)                   | Confidential   | Integrity-critical: decides which node processes which resource                           |
| Resource list (`{group}/resources/*` children)                       | Internal       | Integrity-critical: adding or removing children adds or removes work                      |
| Coordination state (`clients/c_*`, `epoch`, `status`, `stopped/*`, barriers) | Internal | Integrity- and availability-critical: leader election, fencing (epoch), barrier progress |
| Session credentials (`authData`)                                    | Restricted     | Secret; grants full control of the group's provider-created znodes                       |
| Node identifiers and resource names in logs                         | Internal       | May reveal topology                                                                       |

## Data flows

1. Node → ZooKeeper session (TCP) → ensemble – connect, then `addAuthInfo(authScheme, authData)` when configured.
2. Node → `create` → `{group}/clients/c_<seq>` (ephemeral sequential) – registration and leader election.
3. Node → `create` (if missing) → `{group}/clients`, `epoch`, `status`, `stopped`, `resources` – created with the
   configured ACL. A znode that already exists keeps its ACL.
4. Coordinator → `setData` → `epoch` (fencing), `status` (GlobalBarrier), `resources` (assignment table, versioned).
5. Followers → `getData` / watches → `status`, `resources` – read their assignment.
6. Followers → `create` / `delete` → `stopped/<id>` (GlobalBarrier) or `resources/<r>/barrier` (ResourceBarrier).
7. Operator / tooling → `create` / `delete` → `{root}`, `{group}`, `{group}/resources`, `{group}/resources/<r>` –
   provisioning, outside the provider's ACL.

## Threat analysis (STRIDE)

### Spoofing

- Threat: a client that is not part of the group registers under `clients` and wins or influences leader election.
- Affected flow/asset: flows 2, 4; coordination state.
- Mitigation(s): ACL overload with digest auth (ADR 0006). Only sessions with the group's credentials can create
  children under a provider-created `clients` znode.
- Risk: **High** with the default `OPEN_ACL_UNSAFE`; **Low** with ACLs, if `clients` was created by a credentialed node.

### Tampering

- Threat T1: rewrite the assignment table so that two nodes process the same resource, or none does.
- Affected flow/asset: flow 4; `resources` znode data.
- Mitigation(s): provider ACLs only help if the `resources` znode was created by a credentialed node or secured with
  `setAcl`. Operators usually pre-create `resources` to list resource children, so it often keeps an open ACL.
- Risk: **High** whenever `resources` has an open ACL, **even with the ACL feature enabled**. **Low** once
  `resources` is secured with the group's credentials.
- Threat T2: add or remove resource children, changing the set of work.
- Mitigation(s): same as T1; secure `resources` with the group's credentials.
- Risk: **Medium**.
- Threat T3: rewrite `epoch` or `status` to defeat fencing or stall barriers.
- Mitigation(s): ACLs on provider-created znodes.
- Risk: **High** without ACLs; **Low** with ACLs.

### Repudiation

- Threat: an unauthorized change to assignments cannot be attributed.
- Mitigation(s): ZooKeeper has no per-change audit by default. With digest ACLs the writing identity is at least
  limited to holders of the group's credentials. Shared credentials mean nodes cannot be told apart.
- Risk: **Medium**. Accepted for a coordination library; audit belongs to the ensemble (server audit logging, where
  available; unverified – please confirm for the deployed ZooKeeper version).

### Information Disclosure

- Threat I1: anyone with ensemble access reads resource names, node identifiers and assignments.
- Mitigation(s): ACLs. With `CREATOR_ALL_ACL`, read access needs the credentials. ZooKeeper 3.8 also requires
  read permission to read a znode's ACL (verified 2026-10-18).
- Risk: **Low to Medium**, depending on how sensitive resource names are.
- Threat I2: digest credentials are disclosed on the network or in configuration.
- Mitigation(s): encrypt client-to-server traffic (out of scope here); load `authData` from a secret store; never log it.
  Rebalancer does not log `authData`.
- Risk: **Medium** without transport encryption.

### Denial of Service

- Threat D1: delete or flood `clients`, `stopped` or barrier znodes to stall rebalancing indefinitely.
- Mitigation(s): ACLs on provider-created znodes; ensemble quotas and network isolation.
- Risk: **High** without ACLs; **Low** with ACLs.
- Threat D2: misconfigured credentials (nodes with different credentials) make nodes fail with `NoAuth` and abort.
- Mitigation(s): operator notes in `src/Rebalancer.ZooKeeper/README.md` (same credentials on every node); the
  change record requires all nodes to run the new version before ACLs are enabled.
- Risk: **Medium** (operational).

### Elevation of Privilege

- Threat: a node with the group's credentials can control every znode of the group, including other nodes'.
- Mitigation(s): none within one group. All nodes share one identity by design (`CREATOR_ALL_ACL` requires it).
  Use separate credentials per resource group to contain a compromised application.
- Risk: **Medium**. Accepted: a compromised node can already mishandle the resources it is assigned.

## Controls mapping

| Threat / Risk                                     | Control (design or runtime)                                                                  | Evidence / Reference                                                                 |
|---------------------------------------------------|----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| Spoofing / tampering of provider-created znodes   | Digest auth + `CREATOR_ALL_ACL` via the ACL overloads                                         | ADR 0006; `tests/Rebalancer.IntegrationTests/ZooKeeper/AclTests.cs`                  |
| Tampering of the assignment table (`resources`)   | Create `resources` with the group's credentials, or `setAcl` it before starting secured nodes | `src/Rebalancer.ZooKeeper/README.md` (operator notes); detected and logged once per session (ADR 0010), **not enforced** |
| Credential disclosure                             | Secret store for `authData`; transport encryption at the ensemble                              | README operator notes; deployment responsibility                                      |
| Misconfiguration DoS                              | Same credentials on every node; all-nodes-first upgrade before enabling ACLs                  | CR-2026-10-18 (Implementation plan, step 5)                                          |

## Residual risk and acceptance

- Residual risks after mitigations:
  - **ACLs stay opt-in.** The default remains `OPEN_ACL_UNSAFE` for compatibility, so unconfigured deployments keep
    every High risk above.
  - **The library does not secure or check the operator-created `resources` znode,** which holds the assignment
    table. A deployment can enable ACLs and still have world-writable assignments.
  - **No per-node identity or audit trail** within a resource group.
- Proposed follow-ups (each needs a decision record before implementation):
  1. When an auth scheme is configured, warn or fail if `{group}/resources` (or the group node) has an ACL that
     grants write access to `world:anyone`.
  2. Make ACLs the default in a future major version.
- Decision: Accepted by dradoaica (maintainer, service owner and security / architecture reviewer) on 2026-10-18,
  including the residual risks above.

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md` (Accepted); follow-up 1:
  `docs/adrs/0010-warn-on-open-operator-znodes-when-acls-are-configured.md`, follow-up 2:
  `docs/adrs/0011-zookeeper-acls-by-default-in-next-major-version.md` (both Proposed).
- Required SDPs: N/A (library).
- Change records to implement mitigations: `docs/change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md`.
- Runbooks / monitors to add: an operator procedure to create or secure a resource group's znodes with the group's
  credentials (ZooKeeper CLI `addauth` + `create` / `setAcl`).

## Approvals

| Role                    | Name | Date |
|-------------------------|------|------|
| Service owner           | dradoaica | 2026-10-18 |
| Security / architecture | dradoaica | 2026-10-18 |

## Sources

- ADRs: `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`,
  `docs/adrs/0006-provider-resource-ownership-and-zookeeper-acl.md`
- SDP: N/A
- Related user stories: `docs/user-stories/US-007-coordinate-via-zookeeper-with-barrier-mode.md`
- Change records: `docs/change-records/CR-2026-10-18-coordination-safety-and-provider-ownership.md`
- Code: `src/Rebalancer.ZooKeeper/Zk/ZooKeeperService.cs` (`InitializeGlobalBarrierAsync`,
  `InitializeResourceBarrierAsync`, `EnsurePathAsync`, `SetResourcesAsync`)
- External standards: STRIDE (Microsoft threat modeling); OWASP ASVS V4 (access control) – mapping to specific control
  IDs not done (unverified – please confirm if required)
