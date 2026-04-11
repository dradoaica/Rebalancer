# Change records

Change records live in this folder: `docs/change-records/`.

Use the template:

- `change-record-template.md`

## Naming

Recommended patterns:

- `CR-YYYY-MM-DD-short-title.md`
- `YYYY-MM-DD-short-title.md`

## Statuses (meaning)

- **Draft**: being written; risk, plan, and rollback may be incomplete.
- **Submitted**: ready for review/approval.
- **Approved**: approved to execute (per your org gate).
- **Implemented**: executed; validation completed.
- **Rolled back**: rollback executed; capture learnings.
- **Closed**: follow-ups recorded; no further action required.

## Review expectations (human vs agent)

- Agents (typically DevSecOps / systems roles) **draft** change records.
- Humans **review/approve** before executing changes with production/shared-environment impact.

## Must-haves per change record

- Clear summary + motivation
- Scope (in/out)
- Impact assessment
- Step-by-step implementation plan
- Validation plan (how we know it worked)
- Rollback plan (how we restore prior state)
- Approvals section
- `Sources` links to the user story and decision records that authorize it

