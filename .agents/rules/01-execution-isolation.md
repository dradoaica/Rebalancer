# Rule 01 - Multi-agent execution isolation

## Scope

Applies whenever more than one agent works concurrently on the same repository (an orchestrator plus one or more
subagents). Covers where each agent does its work and who may write to git.

## Rule statements

- Each agent **MUST** work in its **own git worktree on its own branch**. Agents **MUST NOT** share a working tree, so
  one agent's uncommitted changes can never disrupt another's.
- Only the **orchestrator MUST** commit and merge locally ("git monopoly"). Subagents produce changes on their isolated
  branches and hand them off; they **MUST NOT** merge into a shared or integration branch.
- The orchestrator **MUST** integrate subagent branches only after resolving contradictions between their outputs (see
  `.agents/protocols/multi-agent-orchestration.md` → Orchestrator responsibilities) and **MUST NOT** silently merge
  conflicting outputs.
- Merging or pushing to any **shared** branch, and opening an MR, remain subject to the human-approval gates in
  `.agents/AGENTS.md` and to all applicable pre-MR checks (full test suite, regression, static analysis, and
  baseline-drift abort where relevant). Checks marked `N/A` require a rationale and human acceptance.
- No agent may approve the resulting MR or deploy — see `.agents/rules/00-no-self-approval.md`.

## Rationale

Concurrent agents sharing one working tree overwrite each other and produce non-reproducible merges. Per-agent
worktrees give isolation; a single committing orchestrator gives one auditable integration point and one place to detect
and resolve conflicts. This mirrors how a human lead integrates several contributors' branches rather than letting
everyone push to the same tree.

## Examples

- **Good:** Orchestrator creates `agent/story-123-api` and `agent/story-123-tests` worktrees, dispatches one subagent to
  each, collects both branches, resolves overlap, runs the pre-MR gate, and opens one MR for human approval.
- **Bad:** Two subagents edit files in the same checkout; the second agent's commit clobbers the first's uncommitted
  work, and each subagent pushes its own branch straight to the shared remote.

## Enforcement

- Human reviewer confirms the MR came from an orchestrator-integrated branch, not from subagents writing to a shared
  tree or merging independently.
- Branch/worktree naming makes per-agent isolation visible in `git worktree list` and branch history.
- All governance rules are human-verified at the review boundary (see `.agents/AGENTS.md` → Governance Guardrails).

## Sources

- `.agents/protocols/multi-agent-orchestration.md`
- `.agents/AGENTS.md` (section: Co-Governance Protocol)
- `.agents/rules/00-no-self-approval.md`
- Framework improvement incorporating external multi-agent practitioner feedback (2026-10-14).
