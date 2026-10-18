# Problem records

Problem records live in this folder: `docs/problem-records/`.

Use the template:

- `problem-record-template.md`

## Naming

- `PRB-NNNN-kebab-case-title.md` (e.g., `PRB-0001-nightly-import-timeouts.md`); front matter `id`: `PRB-NNNN`.
- `NNNN` is the next free number on the **shared trunk**, never reused (see
  `.agents/protocols/multi-human-collaboration.md`).

## Metadata

Copy the front matter from `problem-record-template.md` and keep every key.

- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Open**: a problem recognized; scope and impact defined.
- **Investigating**: evidence collection and hypotheses in progress.
- **Root cause identified**: root cause documented; corrective actions are defined.
- **Closed**: corrective and preventive actions completed (or explicitly accepted/declined).

## Review expectations (human vs. agent)

- Agents **draft** problem records when issues are systemic, recurring, or risk-bearing.
- Humans **review** to ensure the record reflects reality and commitments (owners/actions) are appropriate.

## Must-haves per problem record

- Symptoms and impact
- Evidence (links/queries/tickets) and timeline
- Root cause (or explicitly unknown) + contributing factors
- Corrective actions and preventive measures (tests, monitoring, runbooks)
- Links to related change records and decision records

