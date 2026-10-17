# Skills (approved procedures)

Skills are **step-by-step procedures** for recurring tasks (drafting an ADR, creating a change record, running a threat
model, etc.). Skills are meant to be followed **exactly**.

## Must-haves

- Each skill must be:
    - **Deterministic** (no "do what you think best" steps)
    - **Gated** (states what approvals/artifacts must exist first)
    - **Auditable** (produces concrete outputs)
    - **Human-friendly** (a human can execute it without AI)

## Approved repository skills

- **`ponytail`** (`.agents/skills/ponytail/SKILL.md`): Pragmatic senior developer execution mode (YAGNI, standard
  libraries, minimal complete diffs, ceiling annotations).
- **`ponytail-review`** (`.agents/skills/ponytail-review/SKILL.md`): Quality and lean diff review (correctness, safety,
  scale, tests, bloat).
- **`ponytail-audit`** (`.agents/skills/ponytail-audit/SKILL.md`): Repository-wide quality, scalability, and debt audit.
- **`ponytail-debt`** (`.agents/skills/ponytail-debt/SKILL.md`): Harvests shortcut/deferral comments into an auditable
  technical debt ledger.
- **`ponytail-gain`** (`.agents/skills/ponytail-gain/SKILL.md`): Empirical benchmark scoreboard display.
- **`ponytail-help`** (`.agents/skills/ponytail-help/SKILL.md`): Reference card for commands and configurations.

## Skill file requirements

Every skill file must include:

- **Purpose**
- **Inputs required**
- **Preconditions / governance gates**
- **Procedure** (numbered steps, no skipping or reordering)
- **Outputs** (file paths)
- **Validation** (how to confirm correctness)
- **Escalation points** (where the skill must pause for human input)
- **Sources**

## Template

```markdown
# <Skill name>

## Purpose

## Inputs required

## Preconditions / governance gates

## Procedure (mandatory)

## Outputs

## Validation

## Escalation points

## Sources
```
