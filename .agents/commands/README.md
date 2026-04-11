# Commands (project task shortcuts)

Commands are **repeatable, strictly-scoped task specs** that an agent can execute without guessing.

## Must-haves

- Every command file must include:
    - **Purpose**
    - **Inputs required** (explicit list)
    - **Preconditions / gates** (what must already exist/approved)
    - **Steps** (ordered, no optional ambiguity)
    - **Outputs** (exact files/artifacts produced)
    - **Acceptance criteria** (how a human verifies success)
    - **Safety constraints** (what the agent must not do)
    - **Sources** (ADR/spec/change record references)

## Naming and structure

- **File naming**: `cmd-<verb>-<object>.md` (e.g., `cmd-draft-adr.md`)
- **One command = one outcome**: keep each command single-purpose.
- **No hidden work**: if a command needs other artifacts (ADR, change record), it must declare them as preconditions.

## Template

Create new command files by copying this:

```markdown
# <Command name>

## Purpose

## Inputs required

## Preconditions / gates

## Steps (mandatory)

## Outputs

## Acceptance criteria

## Safety constraints

## Sources
```
