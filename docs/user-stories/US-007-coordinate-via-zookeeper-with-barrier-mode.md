# User story: Coordinate via ZooKeeper with selectable barrier mode

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-007                                             |
| **Status**      | Done                                               |
| **Priority**    | Must                                               |
| **Owner**       |                                                    |
| **SSDLC phase** | Maintenance (as-built documentation)             |

## Story

**As a** distributed systems operator  
**I want** to run `ZooKeeperProvider` with either `ResourceBarrier` or `GlobalBarrier` rebalancing mode  
**So that** I can choose the algorithm that fits my scale and latency tradeoffs

## Context

`RebalancingMode` enum and `ZooKeeperProvider` implement two barrier strategies documented in `src/Rebalancer.ZooKeeper/README.md`. Leader election uses ephemeral sequential znodes and epoch/versioning to limit zombie coordinators.

## Acceptance criteria

1. `ZooKeeperProvider` is constructed with parameters including `RebalancingMode` and ZooKeeper connection settings (see constructor in `ZooKeeperProvider.cs`).
2. Behavior differs by mode as described in ADR 0003 and the ZK README (resource-level barriers vs global stop/start choreography).
3. Minimum rebalancing interval and session/connect timeouts are configurable inputs to the provider where exposed by the constructor.

## Non-goals

- Guaranteeing ZooKeeper ACL policies; operators must configure ZK security.
- Etcd or Consul backends (not in this story).

## Dependencies

- Decision records: `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`, `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

ZooKeeper paths may hold assignment metadata; use ZK ACLs and network isolation. Do not expose ZK ensemble to untrusted networks.

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`
- `src/Rebalancer.ZooKeeper/RebalancingMode.cs`
- `src/Rebalancer.ZooKeeper/README.md`
- `README.md`
