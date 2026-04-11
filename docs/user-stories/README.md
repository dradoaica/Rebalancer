# User stories

User stories live in this folder: `docs/user-stories/`.

Use the template:

- `user-story-template.md`

## Naming

Recommended patterns (pick one and stay consistent):

- `US-001-short-title.md`
- `US-<teamKey>-001-short-title.md`

## Statuses (meaning)

- **Draft**: being written/refined; not ready to commit to.
- **Ready**: Definition of Ready met; can be pulled into an iteration.
- **In progress**: being implemented.
- **Done**: acceptance criteria met; release/change record linked if applicable.
- **Cancelled**: intentionally not doing; keep rationale.

## Review expectations (human vs agent)

- Agents (typically persona `agile-user-story-writer`) **draft** stories using the template.
- Humans **review/approve** story readiness before treating it as committed scope (org-specific gate).

## Must-haves per story

- Clear **As a / I want / So that**
- **Testable** acceptance criteria
- Explicit **non-goals**
- **Security & compliance** notes (or “N/A” with rationale)
- `Sources` links to decision records / SDPs / change records when relevant

## v2 slice

Stories [US-012](US-012-register-rebalancer-with-dependency-injection.md)–[US-014](US-014-traceability-matrix-and-product-baseline.md) are **Done** (DI extensions, `tests/Rebalancer.IntegrationTests` / `tests/Rebalancer.UnitTests`, documentation). See [traceability matrix](../traceability-matrix.md).

