# Governance artifacts: what to draft, and who drafts it

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

## Governance decision tree (what to draft when)

If the required governance artifact is missing, **you** draft it (under the matching persona). If the human asked for
work, follow this decision tree:

- **Is this a new or changed decision that is expensive to reverse?**
    - Yes → **Draft a decision record** (typically an ADR) in `docs/adrs/` (blocking gate).
- **Is this proposing a change to an existing service / process with operational risk?**
    - Yes → **Draft a change record** in `docs/change-records/` (include rollback plan).
- **Is this defining or changing a service offering (SLAs, support model, transition plan)?**
    - Yes → **Draft an SDP** in `docs/sdps/`.
- **Is this recurring pain, incidents, or "unknown root cause" quality/security issues?**
    - Yes → **Draft a problem record** in `docs/problem-records/`.
- **Is it small, reversible, and purely editorial (typos, formatting) with no semantic change?**
    - Yes → Proceed, but still cite the files you changed.

When in doubt between decision record vs. change record:

- **Decision record** decides *what/why* (architecture/approach, immutable once accepted).
- **Change record** governs *execution* (approvals, scheduling, rollback).

## Artifact ownership (personas)

Agents **author drafts**; humans **review and approve** when a gate requires it. Use the persona files in
`.agents/agents/` for role-specific constraints. Typical mapping:

| Persona (file in `.agents/agents/`)                                         | Primary artifacts to draft                                                                                           |
|-----------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------|
| `agile-user-story-writer.md`                                                | User stories in `docs/user-stories/`                                                                                 |
| `enterprise-architect.md`, `solutions-architect.md`, `systems-architect.md` | Decision records, architecture notes, SDPs (as appropriate)                                                          |
| `devsecops-engineer.md`                                                     | Change records (ops/pipeline/security sections), CaC/IaC-related proposals                                           |
| `full-stack-developer.md`, `mobile-developer.md`                            | Implementation **against** approved artifacts; may draft supporting stories or ADR input—not unilateral architecture |
| `qa-automation-engineer.md`                                                 | Test plans, quality criteria, automation proposals traceable to stories/specs                                        |
| `orchestrator.md`                                                           | Task decomposition plans, subagent dispatch specs (`.agents/commands/cmd-dispatch-subagent.md`), integrated MRs/PRs  |

If a persona file is empty, still follow this table and `.agents/AGENTS.md` until the persona is filled in.
