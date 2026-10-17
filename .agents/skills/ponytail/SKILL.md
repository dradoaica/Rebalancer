---
name: ponytail
description: >
  Lazy senior dev mode: the smallest change that fully solves the task, and a
  reply a busy human understands in one read. Use on any coding task (writing,
  fixing, refactoring, reviewing, choosing dependencies) and when the user says
  "ponytail", "be lazy", "simplest solution", "yagni", or complains about
  over-engineering or bloat. Levels: lite, full (default), ultra.
argument-hint: "[lite|full|ultra|off]"
license: MIT
---

# Skill: Ponytail (Pragmatic Senior Engineer Execution)

## Purpose

Applies a pragmatic, senior-developer mindset to code changes: produces the smallest complete change that fully
satisfies the task, eliminates premature abstractions and speculative bloat, enforces standard-library-first
implementations, and leaves clear traceability for shortcuts.

## Inputs required

- Active task context, bug report, user story (`docs/user-stories/`), or accepted ADR (`docs/adrs/`).
- Active codebase, callers, callees, and test fixtures.
- Optional level parameter (`lite`, `full` [default], `ultra`, `off`).

## Preconditions / governance gates

- **Governance Alignment:** Must adhere to `.agents/AGENTS.md` and the active change tier in the Change Classification
  Triage Matrix.
- **Tier 1 Gate:** Ponytail cannot bypass the requirement for an **Accepted** Architectural Decision Record
  (`docs/adrs/`) if the proposed work involves architectural changes, breaking API contracts, data schema changes, or
  security posture modifications.
- **Rule 00 Compliance:** Follows `.agents/rules/00-no-self-approval.md`—the agent cannot self-approve its own
  implementations.

## Procedure (mandatory)

1. **Map blast radius before writing**:
    - Identify every place the change must reach: callers, tests, fixtures, config, and exports.
    - Verify potential breakage: data destruction, data exposure, or callers that stop working.
2. **Apply the smallest complete solution hierarchy**:
    - Verify if the code needs to exist: skip unrequested features, options, and flexibility.
    - Reuse existing codebase helpers, components, services, and patterns.
    - Standard library or platform features first: exhaust built-ins before reaching for external dependencies.
    - Installed dependencies: use what is already present rather than introducing new packages.
    - Strive for clarity: prefer concise, readable logic; keep values in native platform formats.
    - Preserve structure: keep the repository's layers, interfaces, and conventions; deletion beats addition.
3. **Execute root-cause bug fixes**:
    - Inspect every caller of the touched function and fix the root cause once in shared code.
    - Preserve error handling and input validation at trust boundaries.
4. **Annotate deliberate trade-offs and limits**:
    - When choosing an intentional simplification or shortcut with a known ceiling, annotate it explicitly (per
      `.agents/AGENTS.md` → Technical debt governance):
      `# shortcut: <the limit>, <upgrade trigger> (risk: <low|med|high>, track: <US-…|PRB-…>)`
    - The `track:` item must exist (user story in `docs/user-stories/` or problem record in `docs/problem-records/`);
      if it does not, draft it. A shortcut without a `track:` reference is undocumented debt and is not allowed.
5. **Add proportional validation**:
    - For new non-trivial logic (branching, loops, parsers, security, financial calculations), write one focused test or
      assertion verifying behavior. Trivial changes require no extra test scaffolding.
6. **Apply level modifier**:
    - **lite**: Build what was requested; name the smaller option in one line and let the human decide.
    - **full** (Default): Enforce rules 1–5; deliver the minimal diff.
    - **ultra**: Actively challenge the premise before writing code; push back against parts unsupported by actual need.
7. **Format human handoff**:
    - End reply with 1–2 lines stating what was skipped/untested and any known risks.
    - Include the standard governance `### Sources` block.

## Outputs

- Minimal, functional code diff with no speculative generality.
- Targeted unit/regression test for non-trivial logic.
- Documented shortcut comments (`# shortcut: ... (risk: ..., track: ...)`) where ceilings exist.
- Human handoff summary and `### Sources` block.

## Validation

- Execute relevant test suites / regression commands within the isolated runner.
- Confirm no breaking contract changes occurred without an accepted ADR.
- Verify that every caller of modified functions remains functional.

## Escalation points

- Pause and prompt for an ADR (`docs/adrs/`) draft if a simplification requires changing a public interface, breaking a
  schema, or altering security boundaries (Tier 1).
- Escalate to the human when requirement ambiguity could lead to multiple conflicting implementations.

## Sources

- Upstream: `DietrichGebert/ponytail`
- Repository Governance: `.agents/AGENTS.md`
- No Self-Approval: `.agents/rules/00-no-self-approval.md`
