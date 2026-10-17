---
name: ponytail-review
description: >
  Quality review of a change: is the logic right, is it safe, does it hold
  under real load, is risky code tested, is it fast enough, and is every line
  needed. Reads the connected code, not only the diff. Each finding is
  explained in plain English. Use for "review this", "code review", "review
  the last commit", "review my PR", "is this over-engineered", /ponytail-review.
---

# Skill: Ponytail Review (Quality & Lean Code Review)

## Purpose

Performs an in-depth, pragmatic code review of proposed changes or pull requests. Evaluates correctness, security,
performance under scale, regression testing, and code bloat, providing actionable findings in clear language.

## Inputs required

- Diff target: uncommitted changes, specific branch, commit hash, or PR reference.
- Connected codebase context (callers, callees, configuration, and tests).

## Preconditions / governance gates

- **Advisory Role Only:** Review findings inform the human reviewer. Under `.agents/rules/00-no-self-approval.md`, this
  review does **not** constitute binding approval or merge authorization.
- **Specification Check:** Verify whether changes align with governing ADRs (`docs/adrs/`) or User Stories
  (`docs/user-stories/`).

## Procedure (mandatory)

1. **Understand context and scope**:
    - Identify the target changes (uncommitted diff, branch, PR).
    - Read surrounding code: callers of modified functions, callees, test suites, and documentation.
    - Trace data flow end-to-end (inputs, persistence, outputs).
    - Determine assumed load/scale based on repository configuration and docs.
2. **Evaluate against quality dimensions**:
    - **Bug:** Wrong output, unhandled edge cases (empty collections, zero, bounds, time zones), broken callers.
    - **Risk:** Security vulnerabilities (injection, auth bypass, secret leaks) and data loss risks (unhandled
      exceptions, lack of transactions).
    - **Scale:** Concurrency bugs, unindexed queries, unbounded in-memory growth, N+1 query loops.
    - **Missing Test:** Risky new logic, security-critical paths, or bug fixes lacking verification tests.
    - **Speed:** Significant algorithmic or I/O bottlenecks.
    - **Lean (YAGNI):** Dead code, redundant abstractions, unnecessary dependencies, duplicate helpers.
3. **Verify findings against concrete scenarios**:
    - Ensure every finding describes a concrete, reproducible failure condition ("Input X leads to failure Y").
    - Confirm shortcut comments (`# shortcut: ...`) are treated as recorded decisions unless current load violates their
      ceiling.
    - Propose the minimal fix that deletes or simplifies code rather than adding wrappers.
4. **Generate structured output**:
    - **What this change does**: 2–3 plain English summary sentences.
    - **Findings**: Grouped by priority:
        - `Must fix`: Bugs, security vulnerabilities, data loss, scale breakages.
        - `Should fix`: Risky code without tests, performance regressions, unnecessary code.
        - `Nice to have`: Micro-optimizations, cleaner syntax.
    - Number findings continuously across groups with 4-part structure: *What this is*, *Problem*, *Fix*, *If we skip
      it*.
    - **Verdict**: `Verdict: Ship.` or `Verdict: fix <numbers> first.`
    - **Metrics**: `Lean: -<N> lines possible.` (if applicable) and `Not checked:` for unreviewed areas.

## Outputs

- Structured review report formatted in plain English.
- No direct code edits are performed during review.

## Validation

- Confirm every reported issue corresponds to verified lines and realistic execution paths.
- Ensure the review does not flag stylistic preferences or hypothetical issues lacking a failure case.

## Escalation points

- Flag immediate governance violation if a Tier 1 change (architectural or contract break) is attempted without an
  approved ADR.
- Stop and hand off the report to the human approver without self-merging.

## Sources

- Upstream: `DietrichGebert/ponytail`
- Repository Governance: `.agents/AGENTS.md`
- No Self-Approval: `.agents/rules/00-no-self-approval.md`
