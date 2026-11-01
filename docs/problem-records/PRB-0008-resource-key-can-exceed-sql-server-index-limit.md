---
id: PRB-0008
status: Closed
date: 2026-11-02
opened: 2026-11-01
owner: "dradoaica"
approved-by: [ "dradoaica" ]            # closure review; set by a human only (dradoaica, in session 2026-11-02)
severity: "Low"
depends-on: [ "ADR-0032", "CR-0013", "CR-0014" ]
---

# Problem record: a SQL Server resource key can exceed the index key limit

## Summary

The shipped SQL Server table `RBR.Resources` has the clustered primary key `QueuesPK` on `ResourceGroup VARCHAR(100)`
and `ResourceName VARCHAR(1000)`: up to 1100 bytes, above SQL Server's 900-byte limit for a clustered index key.
`CREATE TABLE` only warns; an insert whose group and resource name together exceed 900 bytes fails.

## Symptoms & impact

- **Symptoms:** running `db/sqlserver/RebalancerTables.sql` prints "The maximum key length for a clustered index is 900
  bytes. The index 'QueuesPK' has maximum length of 1100 bytes."; an operator inserting a resource with a long name
  gets an error.
- **Impact:** operators only (resources are provisioned by an operator, not by nodes). Coordination is not affected:
  names that fit are stored and read as before. PostgreSQL has no such limit (`db/postgresql/RebalancerTables.sql`).

## Timeline

| When       | Event                                                                                                |
|------------|------------------------------------------------------------------------------------------------------|
| 2026-11-01 | Detected: the 5.0.0 rollback rehearsal installed the shipped scripts and SQL Server warned (CR-0013) |
| 2026-11-01 | Confirmed by the post-release cross-check (the arithmetic; not reproduced with an over-long insert)  |
| 2026-11-02 | Reproduced: 900 bytes accepted, 901 rejected with error 1946 (`SqlServerSchemaTests`); fix decided   |

## Evidence

- `db/sqlserver/RebalancerTables.sql`: `ResourceGroup VARCHAR(100)`, `ResourceName VARCHAR(1000)`,
  `CONSTRAINT QueuesPK PRIMARY KEY CLUSTERED (ResourceGroup ASC, ResourceName ASC)`.
- The SQL Server warning above, printed when the script ran in the rehearsal (CR-0013, Preconditions).

## Root cause analysis

The column sizes and the key predate the governance records. Until 5.0 the SQL Server tests did not use the shipped
scripts: their own schema had `ResourceName VARCHAR(255)`, which fits, so the warning never appeared in a test run (ADR-0032 moved the tests to the shipped scripts).

### Contributing factors

- A test-only schema that differed from the shipped one (removed by ADR-0032).

## Corrective actions

| Action                                                                                                         | Owner     | Status                                 |
|----------------------------------------------------------------------------------------------------------------|-----------|----------------------------------------|
| Decide the fix (schema change, Tier 1): shorten `ResourceName` so the key fits, or document the 900-byte limit | dradoaica | Done: document (CR-0014, Approved)     |
| A test that inserts a resource at the documented maximum length, on the shipped scripts                        | dradoaica | Done: `SqlServerSchemaTests` (CR-0014) |

## Preventive measures

- **Agent Governance:** N/A: the contributing factor (a test-only schema) is already removed by ADR-0032.
- **Tests / automation:** the corrective test above.
- **Monitoring / alerts:** N/A (library).
- **Runbooks / docs:** state the maximum resource name length in the README's SQL Server section (done, CR-0014).
- **Decision records:** needed if the column changes (Tier 1, data schema).

## Known errors / workarounds

Keep resource names, together with the group name, under 900 bytes on SQL Server.

## Closure

- **Closed:** 2026-11-02, closure reviewed and approved by dradoaica; the fix ships in 5.1.0 (CR-0014)
- **Resolution summary:** documented, no schema change: on SQL Server a resource's group and name together fit in 900
  bytes (README, "Operating the backends"). Shortening `ResourceName` would be a Tier 1 schema change for existing
  databases, for names nobody has reported as too long.
- **Preventive action verification:** `SqlServerSchemaTests.AResourceKey_FitsUpTo900Bytes_AndALongerOneIsRejected`
  passes on the shipped scripts (2026-11-02).

## Sources

- `docs/adrs/0032-no-foreign-key-from-client-records-to-resource-groups.md` (the tests now use the shipped scripts)
- `docs/change-records/CR-0013-release-5-0-0.md` (Preconditions, rollback rehearsal)
- `docs/change-records/CR-0014-release-5-1-0.md` (Scope: B6 / PRB-0008)
