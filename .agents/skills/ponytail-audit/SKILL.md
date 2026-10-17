---
name: ponytail-audit
description: >
  Quality audit of a whole repo: bugs, security holes, what breaks under real
  load, risky code without tests, slow paths, and what to delete, merge or
  split. Ranked, each finding explained in plain English. One-shot report,
  changes nothing. Use for "audit this codebase", "review the whole repo",
  "find bloat", "what can I delete", /ponytail-audit.
---

# Skill: Ponytail Audit (Repository-Wide Quality & Bloat Audit)

## Purpose

Conducts a comprehensive, read-only quality and maintainability audit across the entire codebase or specified subsystem.
Identifies dormant bugs, security vulnerabilities, scalability limits, missing tests, and architectural bloat.

## Inputs required

- Target scope: specific directory, module, or entire repository.
- System entry points, architecture docs (`docs/adrs/`), dependency lists, and test suites.

## Preconditions / governance gates

- **Read-Only Inspection:** Generates an informational report; does not modify code or configuration.
- **Traceability:** References existing architecture documents when identifying structural deviations.

## Procedure (mandatory)

1. **Map system architecture & entry points**:
    - Read system manifests (README, build configs, dependencies, package files).
    - Identify primary entry points (routes, controllers, background jobs, CLIs).
    - Determine baseline operational scale from deployment and configuration files.
    - Map critical data flows (user input, authentication, persistence, cross-process communication).
2. **Scan for defects and complexity**:
    - **Bugs & Inconsistencies:** Inconsistent rule applications, unhandled edge cases, broken assumptions.
    - **Security & Data Safety:** Missing input sanitization, insecure defaults, unmanaged secrets, uncommitted
      transactions.
    - **Scalability & Concurrency:** Race conditions, unbounded memory structures, N+1 query patterns.
    - **Untested Risk:** High-consequence logic lacking automated regression coverage.
    - **YAGNI & Bloat:** Dead modules, unused config flags, single-implementation interfaces, external dependencies
      replaceable by standard libraries.
3. **Verify and rank findings**:
    - Ensure every reported item includes a concrete problem scenario and minimal fix.
    - Respect annotated shortcuts (`# shortcut: ...`) unless current system requirements exceed their limit.
    - Rank findings by severity: `Must fix`, `Should fix`, `Nice to have` (capped at 20 most impactful findings).
4. **Render structured audit report**:
    - **What this repo does**: 2–3 sentences defining core functionality and assumed load.
    - **Grouped Findings**: 4-part details (*What this is*, *Problem*, *Fix*, *If we skip it*).
    - **Verdict**: Overall health summary.
    - **Lean Opportunity**: `Lean: -<N> lines, -<M> dependencies possible.`
    - **Coverage Disclosure**: Explicitly list paths/modules that were not inspected.

## Outputs

- Comprehensive, prioritized audit report.
- Zero repository modifications.

## Validation

- Confirm all code references exist and can be traced to active files.
- Verify that potential deletions do not break dynamic or reflection-based usages.

## Escalation points

- Identify high-severity security vulnerabilities or severe data integrity risks for immediate human remediation.
- Recommend drafting Problem Records (`docs/problem-records/`) or ADRs (`docs/adrs/`) for large-scale structural debt.

## Sources

- Upstream: `DietrichGebert/ponytail`
- Repository Governance: `.agents/AGENTS.md`
- TOGAF ADRs: `.agents/concepts/togaf/architectural-decision-records.md`
