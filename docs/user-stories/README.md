# User stories

User stories live in this folder: `docs/user-stories/`.

Use the template:

- `user-story-template.md`

## Naming

- `US-NNNN-kebab-case-title.md` (e.g., `US-0001-export-orders-csv.md`); front matter `id`: `US-NNNN`.
- `NNNN` is the next free number on the **shared trunk**, never reused (see
  `.agents/protocols/multi-human-collaboration.md`).

## Metadata

Copy the front matter from `user-story-template.md` and keep every key.

- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Draft**: being written/refined; not ready to commit to.
- **Ready**: Definition of Ready met; can be pulled into an iteration.
- **In progress**: being implemented.
- **Done**: acceptance criteria met; release/change record linked if applicable.
- **Canceled**: intentionally not doing; keep rationale.

## Review expectations (human vs. agent)

- Agents (typically persona `agile-user-story-writer`) **draft** stories using the template.
- Humans **review/approve** story readiness before treating it as committed scope (org-specific gate).

## Must-haves per story

- Clear **As a / I want / So that**
- **Testable** acceptance criteria
- Explicit **non-goals**
- **Security & compliance** notes (or "N/A" with rationale)
- `Sources` links to decision records / SDPs / change records when relevant

