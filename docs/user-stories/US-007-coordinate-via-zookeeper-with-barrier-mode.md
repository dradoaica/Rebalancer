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
**So that** I can choose the algorithm that fits my scale and latency trade-offs

## Context

`RebalancingMode` enum and `ZooKeeperProvider` implement two barrier strategies documented in `src/Rebalancer.ZooKeeper/README.md`. Leader election uses ephemeral sequential znodes and epoch/versioning to limit zombie coordinators.

## Acceptance criteria

1. `ZooKeeperProvider` is constructed with parameters including `RebalancingMode` and ZooKeeper connection settings (see constructor in `ZooKeeperProvider.cs`).
2. Behavior differs by mode as described in ADR 0003 and the ZK README (resource-level barriers vs. global stop/start choreography).
3. The `ZooKeeperProvider` constructor takes `sessionTimeout`, `connectTimeout`, and `minimumRebalancingInterval` (`TimeSpan`) and
   passes them to the ZooKeeper session and the coordinator/follower roles; `AddRebalancerZooKeeper` accepts them as optional
   arguments that default to 15 s, 10 s, and 1 s.

## Non-goals

- Guaranteeing ZooKeeper ACL policies; operators must configure ZK security.
- etcd or Consul backends (not in this story).

## Dependencies

- Decision records: `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`, `docs/adrs/0001-pluggable-rebalancer-provider-and-client-lifecycle.md`
- Other stories: `docs/user-stories/US-001-register-provider-and-join-resource-group.md`

## Security & compliance

ZooKeeper paths may hold assignment metadata; use ZK ACLs and network isolation. Do not expose ZK ensemble to untrusted networks.

## Definition of ready

<!-- Added 2026-10-18 to align with the user-story template (DoR section introduced in template v1.0.7). Ticked items
were verified by an agent against this story's text on 2026-10-18; the human readiness review is still to be recorded. -->

- [x] Governing ADR referenced (or marked "N/A - non-architectural")
- [x] Acceptance criteria are testable, unambiguous, and numbered
- [x] Security & compliance considerations addressed
- [x] Dependencies and non-goals are explicitly identified

## Definition of done

<!-- 2026-10-18: "Acceptance criteria met" and "Docs" ticks verified by an agent against the code and docs; "Tests / checks
agreed with a team" confirmed by dradoaica on 2026-10-18; change-record traceability is ticked once the release ships. -->

- [x] Acceptance criteria met
- [x] Tests / checks agreed with a team
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `docs/adrs/0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md`
- `src/Rebalancer.ZooKeeper/ZooKeeperProvider.cs`
- `src/Rebalancer.ZooKeeper/RebalancingMode.cs`
- `src/Rebalancer.ZooKeeper/README.md`
- `README.md`
