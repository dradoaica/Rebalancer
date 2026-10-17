---
name: ponytail-help
description: >
  Quick reference for ponytail levels, skills and commands. One-shot display.
  Use for /ponytail-help, "ponytail help", "how do I use ponytail".
---

# Skill: Ponytail Help (Reference & Command Index)

## Purpose

Provides a quick-reference index of available Ponytail operational modes, skill commands, environment configurations,
and governance relationships.

## Inputs required

- User trigger (`/ponytail-help` or query about ponytail usage).

## Preconditions / governance gates

- **Informational Only:** Displays reference documentation; does not mutate environment or repository settings.

## Procedure (mandatory)

1. **Summarize operational levels**:
    - `lite`: Build requested scope, present minimal option for human decision.
    - `full` (Default): Minimal complete diff, standard libraries, necessary tests, clear handoff notes.
    - `ultra`: Challenge requirement assumptions before writing code.
2. **List available skills and triggers**:
    - `ponytail`: Core pragmatic development skill.
    - `ponytail-review`: Focused code and diff review.
    - `ponytail-audit`: Repository-wide maintainability and bloat audit.
    - `ponytail-debt`: Deferral and shortcut debt scanner.
    - `ponytail-gain`: Empirical benchmark scoreboard.
    - `ponytail-help`: Command reference card.
3. **Document governance interoperability**:
    - Explain how Ponytail functions within `cogov-a-sdlc` (YAGNI execution bounded by ADR and User Story gates).
4. **Display configuration and activation options**:
    - Environment variables (`PONYTAIL_DEFAULT_MODE`).
    - Config file locations and host-specific invocation syntax.

## Outputs

- Formatted reference card.

## Validation

- Verify all documented commands and paths correspond to active skills in `.agents/skills/`.

## Escalation points

- None (informational display).

## Sources

- Upstream: `DietrichGebert/ponytail`
- Repository Governance: `.agents/AGENTS.md`
