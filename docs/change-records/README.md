# Change records

Change records live in this folder: `docs/change-records/`.

Use the template:

- `change-record-template.md`

## Naming

- `CR-NNNN-kebab-case-title.md` (e.g., `CR-0001-rotate-db-credentials.md`); front matter `id`: `CR-NNNN`.
- `NNNN` is the next free number on the **shared trunk**, never reused (see
  `.agents/protocols/multi-human-collaboration.md`).

## Metadata

Copy the front matter from `change-record-template.md` and keep every key.

- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Draft**: being written; risk, plan, and rollback may be incomplete.
- **Submitted**: ready for review/approval.
- **Approved**: approved to execute (per your org gate).
- **Implemented**: executed; validation completed.
- **Rolled back**: rollback executed; capture learnings.
- **Closed**: follow-ups recorded; no further action required.

## Review expectations (human vs. agent)

- Agents (typically DevSecOps / systems roles) **draft** change records.
- Humans **review/approve** before executing changes with production/shared-environment impact.

## Must-haves per change record

- Clear summary + motivation
- Scope (in/out)
- Impact assessment
- Step-by-step implementation plan
- Validation plan (how we know it worked)
- Rollback plan (how we restore the prior state)
- Approvals section
- `Sources` links to the user story and decision records that authorize it

