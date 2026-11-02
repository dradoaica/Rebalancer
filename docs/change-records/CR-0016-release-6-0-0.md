---
id: CR-0016
status: Approved
date: 2026-11-02
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # set by a human only (dradoaica, in session 2026-11-02)
risk: "Medium"
change-type: "Normal"
requested-by: "dradoaica"
planned-window: "Library release 6.0.0, after approval"
depends-on: [ "ADR-0034", "ADR-0036", "ADR-0037", "ADR-0017", "ADR-0026", "TM-0003", "PRB-0009", "CR-0015" ]
---

# Change record: Rebalancer 6.0.0 – Redis scripted writes, internal types

## Summary

Releases 6.0.0 of all eight packages. ADR-0037: the Redis backend makes its conditional writes as server-side Lua
scripts instead of `WATCH` transactions, so writes by different nodes no longer abort each other (PRB-0009's full
fix); the per-group ACL user needs `EVAL` and `EVALSHA` (TM-0003 revision 7). ADR-0034's 6.0 step: the eight types
deprecated in 5.2 are internal. A major release: both changes break hosts (a Redis ACL step; code that used the
types). No stored data, key or format change. Release notes: `docs/release-notes/6.0.0.md`; migration guide:
`docs/release-notes/migrating-to-6.0.md`.

## Motivation / drivers

- PRB-0009: ADR-0036 (5.2.0) reduced the Redis liveness failures over slow links but did not remove them; their cause
  is `WATCH`, which covers whole hashes (PRB-0009, Root cause analysis).
- ADR-0034: the deprecation release (5.2.0) is out; 6.0 completes it.

## Scope

**In scope:**

- **ADR-0037 (Tier 1, accepted):** new `src/Rebalancer.Redis/Utils/RedisScripts.cs`; `RedisHash.CompareAndSetAsync`
  (the leases) compares and sets one field in a script; `ClientService`: keep-alive, status, reset, the coordinator's
  guarded per-record writes (ADR-0036, own record first) and the expiry deletion are scripts. The two one-off
  migrations keep their transactions: the 2.x client move (ADR-0015, once per tenure) and the 3.x per-group key move
  (ADR-0020, `PerGroupKeyMigration`), both on shared keys a per-group user cannot reach; `+@transaction` therefore
  stays in the ACL. The scripts never re-encode a record: Lua's `cjson`
  turns `[]` into `{}` (seen on Redis 7.0 and Valkey 8.1, neither has `decode_array_with_array_mt`); they write
  Newtonsoft's text with the other writers' fields (keep-alive, status or token) filled in as text.
  - Lease guard (ADR-0026) unchanged in meaning: the lease names the coordinator and its token. A coordinator's write
    no longer fails because its own renewal or another node's keep-alive changed a field.
  - ACL: `+eval +evalsha` added to the per-group user; verified sufficient on both servers (not all of `@scripting`).
    StackExchange.Redis sends `EVAL` once, then `EVALSHA`, and tries `SCRIPT LOAD`, which the user is denied and the
    writes do not need (seen with `MONITOR` and `ACL LOG` on Redis 7.0, 2026-11-02).
  - **Deviations from ADR-0037's implementation outline** ("to be confirmed in review"): the scripts are sent with
    `IDatabase.ScriptEvaluateAsync(string)` rather than `LuaScript` (same commands on the wire, see above); a write
    is two or three round trips, not one (a read or `TIME` before the script, so that the decisions stay in .NET);
    the scripts decode JSON with `cjson` but never encode a record (cjson's `[]` → `{}`); and the lease writes keep
    a retry loop for a change of the lease field itself.
  - Tests: `RedisClientRecordTests.AScriptOfThePerGroupUser_CannotReachAnotherGroupsKeys` (declared and undeclared
    keys refused on both servers; TM-0003 revision 7's open point),
    `RecordsWrittenByTheScripts_StayNewtonsoftJson` (byte-identical to Newtonsoft's encoding, with awkward resource
    names); the ACL user tests with the new commands.
- **ADR-0034, 6.0 step (Tier 1, accepted):** `ClientEvent`, `CoordinatorToken`, `EventType`, `AssignmentStatus`,
  Redis `ResourceGroup`, `Resources.Resource`, `Utils.Constants`, SQL Server `Connections.ConnectionHelper` internal;
  `[Obsolete]` and the `RBR0034` suppressions removed; `PublicApiContractTests`.
- **TM-0003 revision 7** (accepted by dradoaica, 2026-11-02): scripting in the ACL user, D2 corrected, D5 (long
  scripts block the instance).
- Versions 6.0.0; docs: release notes, migration guide, README, SECURITY, baseline, overview, `docs/tlaplus/README.md`,
  matrix, PRB-0009, TM-0003 (the verified command set, the data flows, corrections as implemented).

**Out of scope:**

- Lowering the integration-test parallelism in `tests.yml`, if the first "Tests" run shows the runner short of
  memory (CR-0015 follow-up).

**Versioning.** A major release (`.agents/protocols/maintainability.md`, Regression-awareness rules): public types
are removed, and Redis hosts with a per-group ACL user must change their ACL before upgrading.

## Impact assessment

| Area                  | Impact                                                                                                                                                         |
|-----------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Users / customers     | Redis with a per-group ACL user: add `+eval +evalsha` before upgrading. Code that used one of the eight types keeps its own copy. Everything else unchanged.   |
| Services / components | Redis: each conditional write is one script call; the coordinator's writes and the keep-alives no longer retry against each other.                            |
| Data / schema         | None: same keys, layout and JSON.                                                                                                                              |
| Security              | TM-0003 revision 7: the nodes' credentials can run Lua over their group's keys (D5, accepted as Medium on shared instances, Low on a dedicated instance).      |
| Operations            | The ACL step before upgrading; afterwards nodes can be upgraded one at a time (mixed-group rehearsal below). Redis services that forbid scripts stay on 5.2. |

## Preconditions

- [x] Decision records and threat model: ADR-0034, ADR-0037 (Accepted, on `main`); TM-0003 revision 7 (Accepted).
- [x] Build: 0 warnings (2026-11-02).
- [x] Unit tests: 121/121 (2026-11-02).
- [x] Redis suite (all but the network-fault test), Redis 7.0 and Valkey: 59/59 (2026-11-02).
- [x] `RedisNetworkFaultTests`, seed `1196549739`, 10 runs alone on both servers without a failure (ADR-0037
  Confirmation; 5.2.0: 2 failures in 26): **20/20** (2026-11-02), each 1 m 31 s – 1 m 45 s (5.2.0: 2–4 min).
- [x] Mixed group, 5.2.0 and 6.0.0 Redis nodes, assigns and stops correctly (ADR-0037 Confirmation): done
  2026-11-02 with a throwaway harness outside the repository, built once against `ecb4270` (5.2.0) and once against
  this branch (6.0.0), each node a separate process; `redis:7.0`, both versions as the documented 6.0 per-group ACL
  user, one group per scenario seeded in the per-group layout, resources `r1`–`r4`, lease expiry 5 s, heartbeat
  1 s. No two nodes held a resource at once in any scenario:
  - **together:** 6.0 took `r1,r2`, 5.2 `r3,r4`; both stopped cleanly reporting `Stopped`.
  - **5.2 joins a running 6.0 node:** 6.0 held all four, the group rebalanced to two each, 6.0 took all four again
    when 5.2 stopped.
  - **5.2 killed (`SIGKILL`):** 6.0 took all four 10 s later. **6.0 killed:** 5.2 took all four 10 s later (as in
    CR-0013's rehearsal, 10.1–10.3 s).
  - `ACL LOG` denials: the handshake commands as before, `script|load` (see Scope), and reads of the 2.x key
    `Dictionary:RBR:Clients` (the optional 2.x migration, skipped when not permitted, ADR-0015).
- [ ] Integration tests, all but ZooKeeper: (pending).
- [ ] Dependency scan: (pending).
- [x] TLC: **N/A**, accepted by dradoaica (in session 2026-11-02): the model already treats each record write as atomic and fenced (ADR-0036); scripts keep
  that, so the modelled behaviour does not change.
- [x] ZooKeeper integration tests: **N/A**, accepted by dradoaica (in session 2026-11-02): no ZooKeeper code changed.
- [x] Rollback rehearsal: covered by the mixed-group rehearsal (6.0 → 5.2 is the same group with a 5.2 node);
  accepted by dradoaica (in session 2026-11-02).

## Implementation plan

1. Code, tests, docs and version 6.0.0 on branch `agent/release-6.0.0`.
2. Complete the preconditions; record the results here.
3. Merge into `main` (human) and push (human). The push runs the gated release and publishes 6.0.0; the pushed head
   commit must not carry a CI-skip marker.

## Validation plan

- "Release Packages" succeeds, including "Unit Tests" and "Dependency Scan", and pushes the eight packages at 6.0.0.

## Rollback plan

Redeploy 5.2.0; no data or configuration to undo. The ACL commands can stay (5.2 does not use them).

## Communications

Release notes `docs/release-notes/6.0.0.md` and the migration guide `docs/release-notes/migrating-to-6.0.md`.

## Approvals

| Role                   | Name | Date |
|------------------------|------|------|
| Maintainer (code)      | dradoaica | 2026-11-02 (approved in session, before the release) |
| Release / CAB (or org) | dradoaica | 2026-11-02 (approved in session, before the release) |

Approved while the seed replays, the mixed-group rehearsal, the full integration run and the scan were still running;
the approval covers them passing. A failure among them is reported to dradoaica before the merge, not released.

## Post-implementation

- **Actual outcome:** {after the release}
- **Follow-ups:** {after the release}

## Sources

- ADRs: `docs/adrs/0034-public-api-of-the-lease-based-backends-in-6-0.md` (6.0 step),
  `docs/adrs/0036-redis-coordinator-writes-client-records-one-at-a-time.md` (Note: own record first),
  `docs/adrs/0037-redis-guarded-writes-as-server-side-scripts.md` (Decision Outcome, Confirmation),
  `docs/adrs/0017-redis-lease-timing-and-atomic-updates.md`, `docs/adrs/0026-coordinator-acts-only-under-a-valid-lease.md`
- Threat model: `docs/threat-models/TM-0003-redis-coordination.md` (revision 7)
- Problem record: `docs/problem-records/PRB-0009-redis-group-does-not-converge-over-a-lossy-link.md`
- Previous release: `docs/change-records/CR-0015-release-5-2-0.md`
