# Agent personas

This folder holds **specialized agent persona definitions** (one file per role). Before starting a task, identify which
persona applies and adopt its constraints.

## Artifacts: agents draft, humans review

By default, **agents produce** governance artifacts (draft ADRs, user stories, change records, SDPs, problem records)
using the templates under `docs/`. **Humans review and approve** when a gate requires it (e.g., **Accepted** ADR before
implementation, approved change for production). See **Artifact ownership** in `.agents/AGENTS.md` for which persona
typically drafts what.

## How to use

- Read the persona file **in full** before acting under that role.
- **Do not blend personas** in a single response. If work spans roles, split into discrete steps and declare the active
  persona at the start of each step, for example: `[Persona: solutions-architect]`.
- Each persona file is a short **role charter**; the ownership table in `.agents/AGENTS.md` remains authoritative for
  artifact mapping.

## Canonical reference

Authoritative orchestration rules (multi-agent protocol, gates, traceability) live in `.agents/AGENTS.md`.
