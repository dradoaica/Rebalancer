# Skills (approved procedures)

Skills are **step-by-step procedures** for recurring tasks (drafting an ADR, creating a change record, running a threat
model, etc.). Skills are meant to be followed **exactly**.

## Must-haves

- Each skill must be:
    - **Deterministic** (no “do what you think best” steps)
    - **Gated** (states what approvals/artifacts must exist first)
    - **Auditable** (produces concrete outputs)
    - **Human-friendly** (a human can execute it without AI)

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
