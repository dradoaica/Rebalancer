---
name: ponytail-debt
description: >
  List every `shortcut:` comment (and older `ponytail:` ones) as a debt ledger. One-shot report,
  changes nothing. Use for "ponytail debt", "what did ponytail defer", "list the
  shortcuts", /ponytail-debt.
---

# Skill: Ponytail Debt (Shortcut & Technical Debt Ledger)

## Purpose

Scans the codebase for deliberate shortcut annotations
(`# shortcut: <ceiling>, <upgrade trigger> (risk: <low|med|high>, track: <US-…|PRB-…>)` or legacy `# ponytail: ...`)
and compiles them into a structured, auditable technical debt ledger.

## Inputs required

- Target repository or path.
- Optional custom marker pattern (defaults to `shortcut:` and `ponytail:`).

## Preconditions / governance gates

- **Read-Only Ledger:** Produces an auditable report; does not modify code unless explicitly asked to save the ledger
  file.
- **ITIL / Problem Record Alignment:** Identifies unmanaged technical debt that may require elevation to ITIL Problem
  Records (`docs/problem-records/`) or ADRs (`docs/adrs/`).

## Procedure (mandatory)

1. **Execute pattern scan across codebase**:
    - Scan files ignoring build artifacts, caches, and `.gitignore` paths:
      `git grep --no-index --exclude-standard -nE '(#|//|/[*]) ?(shortcut|ponytail):'`
    - If the user requests a custom marker, scan for that marker.
2. **Filter and parse hits**:
    - Discard non-deferral occurrences (e.g., references to keyboard shortcuts).
    - Parse each valid comment into: *File & Line*, *Simplified behavior*, *Ceiling limit*, *Upgrade trigger*, *Risk*,
      *Tracking item*.
3. **Analyze risk and rot**:
    - Identify comments missing an explicit upgrade trigger or ceiling, tagging them with `[no-trigger]` (high risk of
      silent rot).
    - Identify comments missing a `track:` reference (or referencing a non-existent story/problem record), tagging them
      with `[untracked]` (violates `.agents/protocols/maintainability.md` → Technical debt governance).
4. **Format structured ledger**:
    - Output rows grouped by file:
      `<file>:<line>, <what was simplified>. ceiling: <limit>. upgrade: <trigger>. risk: <level>. track: <id>.`
    - Conclude with summary statistics: `<N> markers, <M> with no trigger, <K> untracked.` (or
      `No shortcut debt. Clean ledger.`).
5. **Optional ledger persistence**:
    - If explicitly requested by the user, draft a problem record from `docs/problem-records/problem-record-template.md`
      at `docs/problem-records/PRB-NNNN-tech-debt-ledger.md` (status `Open`), with the ledger as evidence. Allocate
      `NNNN` per `.agents/protocols/multi-human-collaboration.md` → Collision-free identifiers. It stays a draft until
      a human reviews it (Rule 00).

## Outputs

- Structured technical debt report.
- Optional debt ledger file when requested.

## Validation

- Ensure all reported file lines accurately reflect the current source tree.
- Validate that false positives are filtered out.

## Escalation points

- When shortcuts violate active production capacity or security constraints, escalate for an ITIL Problem Record
  (`docs/problem-records/`) or Tier 1 ADR (`docs/adrs/`).

## Sources

- Upstream: `DietrichGebert/ponytail`
- Repository Governance: `.agents/AGENTS.md`
- ITIL Problem Records: `.agents/concepts/itil/problem-records.md`
