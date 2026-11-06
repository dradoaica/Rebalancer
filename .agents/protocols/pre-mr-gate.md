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
7. **Independent review** (Tier 1–2) – a reviewer with no shared context has challenged the change, and the MR/PR
   description carries the result (see below). Run this last, so the reviewer sees the final change.

## Independent review

The agent that wrote a change cannot be the one to find what it wrongly believes. A second agent, seeing the work for
the first time, can.

1. **Isolate the reviewer.** Start a separate agent under `.agents/agents/independent-reviewer.md`, with its model
   class from `.agents/protocols/multi-agent-orchestration.md` → Model assignment. If the tool cannot start one, the
   human opens a new session with no history. Reviewing in the same session does not count. The author changes nothing
   while the reviewer runs, or the reviewer gets its own worktree (`.agents/rules/01-execution-isolation.md`).
2. **Brief it with evidence only.** Give it the diff or artifact, the governing artifacts, and the acceptance criteria.
   Do not give it the conversation, the author's reasoning, or a summary of why the change is right.
3. **Handle findings.** The author fixes a finding or records why it disagrees, then the reviewer re-checks. Stop after
   **2** re-checks. The author does not close a finding it disputes: the finding stays open for the human.
4. **Report it** in the MR/PR description:

```markdown
### Independent review

- **Reviewer:** `[model and class]`, `[subagent / new session]`
- **Given:** `[paths and identifiers]`
- **Re-checks:** `[0–2]`
- **Fixed:** `[findings the author fixed]`, or None
- **Open:** `[each finding, why it matters, and the author's position]`, or None
- **Not checked:** `[what the review did not cover, e.g., tests not run, areas not read]`
```

Open findings do not block the MR/PR: they go to the human, who decides. A clean review is not approval, and it does
not replace human review (`.agents/rules/00-no-self-approval.md`).

## Who approves

The agent that produced the change **must not** approve or merge the resulting MR (`.agents/AGENTS.md` → Co-Governance
Protocol, and `.agents/rules/00-no-self-approval.md`). Under concurrency, only the orchestrator opens the MR
after integrating isolated per-agent branches (see `.agents/protocols/multi-agent-orchestration.md` → Execution
isolation).
