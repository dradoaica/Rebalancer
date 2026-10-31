# Pre-MR gate

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

Opening an MR is where Coding and Testing meet. An agent may open an MR only after all applicable checks below pass.
Use an equivalent control when appropriate; if a check or tool does not apply or is unavailable, record `N/A` with a
rationale and get human acceptance. This does not waive any other applicable quality or security requirement.

1. **Full test suite** green, when applicable.
2. **Regression tests** green (behavior-contract tests, not just the new feature's tests), when applicable.
3. **Static analysis** clean (e.g., Sonar, SAST, secrets scanning), when applicable.
4. **Baseline-drift abort** – where a quality baseline exists (e.g., extraction accuracy, performance, coverage), any
   **negative drift** against the recorded baseline **aborts** the MR; it is not opened until the drift is explained and
   accepted by a human or corrected.
5. **Tier check** – the MR/PR states its tier, and the diff matches it. If the diff touches a "Tier 1 by default" area
   (`.agents/AGENTS.md` → Tier classification rules) without an `Accepted` decision record cited, the MR is not opened.
6. **Governance check** green – `python3 -I scripts/govern_check.py` (add `--base <shared trunk>` when the diff
   touches `docs/`) reports no findings, or each finding is fixed or accepted as `N/A` by a human
   (`.agents/rules/02-governance-checks.md`).

The agent that produced the change **must not** approve or merge the resulting MR (`.agents/AGENTS.md` → Co-Governance
Protocol, and `.agents/rules/00-no-self-approval.md`). Under concurrency, only the orchestrator opens the MR
after integrating isolated per-agent branches (see `.agents/protocols/multi-agent-orchestration.md` → Execution
isolation).
