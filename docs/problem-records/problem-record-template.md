---
# Required metadata (see docs/problem-records/README.md). Keep keys; fill values.
id: PRB-NNNN               # NNNN = the number in this file's name
status: Open               # Open | Investigating | Root cause identified | Closed
date: YYYY-MM-DD           # date of the last status change
opened: YYYY-MM-DD
owner: ""
approved-by: [ ]            # closure review; set by a human only
severity: ""               # per org
depends-on: [ ]             # identifiers, e.g., [CR-0007, ADR-0003]
---

# Problem record: {short title}

## Summary

{What problem is being tracked (not a single incident ticket—recurring or systemic).}

## Symptoms & impact

- **Symptoms:** {what people/systems experience}
- **Impact:** {users, revenue, SLA, security}

## Timeline

| When | Event               |
|------|---------------------|
|      | Detection           |
|      | Mitigation (if any) |
|      |                     |

## Evidence

- Logs / metrics: {links or queries}
- Related incidents / tickets: {ids}
- Related changes: `docs/change-records/…`

## Root cause analysis

{Known root cause, or "Unknown" with hypothesis and what would disprove it.}

### Contributing factors

- {e.g., missing test, operational gap, design debt}

## Corrective actions

| Action | Owner | Status |
|--------|-------|--------|
|        |       |        |

## Preventive measures

- **Agent Governance:** {Update `.agents/rules/`, `.agents/skills/`, or persona if the root cause identifies a governance
  gap; otherwise "N/A" with rationale.}
- **Tests / automation:** {…}
- **Monitoring / alerts:** {…}
- **Runbooks / docs:** {…}
- **Decision records:** {if architecture must change: `docs/adrs/…`}

## Known errors / workarounds

{If applicable: documented workaround for support until fixed.}

## Closure

- **Closed:** YYYY-MM-DD
- **Resolution summary:** {…}
- **Preventive action verification:** {Root-cause-appropriate action completed or explicitly owned/tracked; governance
  change made if a governance gap was identified, or "N/A" with rationale.}

## Sources

- Change records: `docs/change-records/…`
- SDPs / runbooks: `docs/sdps/…`
