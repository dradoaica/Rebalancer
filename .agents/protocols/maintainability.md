# Long-Term Maintainability Standard

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

Research on AI-assisted software maintenance (e.g., SWE-CI benchmark, EvoScore methodology) demonstrates that most AI
models write code that passes tests at the moment of creation but degrades rapidly over later iterations – often
breaking previously working code. This framework is explicitly designed to counter that failure mode.

## Regression-awareness rules

- Every code change must include a statement of which existing behaviors it could affect and why it does not break them.
  This is not optional documentation – it is part of the definition of done.
- Tests must cover the *contract* of a component (inputs → outputs, pre- / post-conditions), not just its current
  implementation. Tests that only verify the internal state are brittle and must be refactored.
- No agent may silently change a public API, exported interface, or configuration schema. Any such change is Tier 1:
  it requires an `Accepted` ADR before implementation, and a change record with a migration path at release.

## EvoScore thinking

Weight long-term consequences more heavily than short-term convenience. Before proposing any solution, ask:

- Will this be understandable to a developer who joins in six months with no context?
- Does this introduce a coupling that will make future changes harder?
- Does this require tribal knowledge to operate correctly?
- Is this testable in a fully automated way, end to end?

If the answer to any of these is "no" or "I am not sure," raise it explicitly before proceeding.

## Human-maintainable infrastructure

All infrastructure, configuration, and deployment automation must satisfy:

- **IaC (Infrastructure as Code):** All cloud resources are declared in version-controlled IaC files. No manual console
  changes. Drift detection is required.
- **CaC (Configuration as Code):** All environment configuration is declared in version-controlled files or a
  secrets-management system. No hardcoded values. No environment-specific logic in source code.
- **Runbooks:** Every operational procedure has a written runbook in the repository that a human can follow without AI
  assistance.
- **No magic:** If an automated process has a non-obvious dependency or ordering requirement, it must be documented
  inline and tested.

## Technical debt governance

Technical debt is never invisible. When a compromise is made (e.g., a TODO, a temporary workaround, a skipped test), it
must be:

1. Documented with a comment that includes: the reason, the risk, and the identifier of the tracking item.
2. Recorded as a user story or problem record.
3. Reviewed at the next iteration planning session.

Agents must not introduce undocumented technical debt.
