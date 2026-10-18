---
# Required metadata (see docs/change-records/README.md). Keep keys; fill values.
id: CR-NNNN                # NNNN = the number in this file's name
status: Draft              # Draft | Submitted | Approved | Implemented | Rolled back | Closed
date: YYYY-MM-DD           # date of the last status change
owner: ""                  # who executes the change
approved-by: [ ]            # set by a human only
risk: ""                   # Low | Medium | High
change-type: ""            # Standard | Normal | Emergency (per org)
requested-by: ""
planned-window: ""         # e.g., 2026-11-02 22:00-23:00 UTC
depends-on: [ ]             # identifiers, e.g., [ADR-0003, US-0012, TM-0002]
---

# Change record: {short title}

## Summary

{One paragraph: what is changing and why.}

## Motivation / drivers

- {Business or technical driver}

## Scope

**In scope:**

- {…}

**Out of scope:**

- {…}

## Impact assessment

| Area                  | Impact |
|-----------------------|--------|
| Users / customers     |        |
| Services / components |        |
| Data / schema         |        |
| Security              |        |
| Operations            |        |

## Preconditions

- [ ] Decision records in place if required: `docs/adrs/…`
- [ ] Tests / dry-run completed: {…}
- [ ] Rollback procedure validated / dry-run tested
- [ ] Pre-change test suite passing cleanly

## Implementation plan

1. {Step}
2. {Step}

## Validation plan

{How we verify success: checks, metrics, smoke tests.}

## Rollback plan

{Concrete steps to restore prior state; who executes.}

## Communications

{Who to notify, when, channels.}

## Approvals

| Role | Name | Date |
|------|------|------|
|      |      |      |

## Post-implementation

- **Actual outcome:** {after change}
- **Follow-ups:** {tickets, problem records if needed}

## Sources

- User stories: `docs/user-stories/…`
- ADRs: `docs/adrs/…`
- SDP: `docs/sdps/…`
- Accepted threat model: `docs/threat-models/…` (when the change affects risk posture)
