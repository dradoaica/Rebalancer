# Multi-Agent Orchestration Protocol

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

When a task requires more than one agent persona, or when an orchestrating agent spawns subagents, follow this protocol
to prevent context pollution, contradictory outputs, and compounding errors.

## Decomposition rules

1. **One cohesive deliverable per agent turn.** Each agent produces one bounded result; it may span multiple related
   files when needed (for example, an implementation slice and its tests). Do not combine unrelated deliverables in one
   turn.
2. **Declare the active persona** at the start of every turn: `[Persona: enterprise-architect]`.
3. **Declare dependencies.** Before starting, list which artifacts from previous turns this turn depends on. If a
   dependency is missing or unapproved, **draft** it or request a handoff—do not proceed on silent assumptions.
4. **Structured handoff schema.** At the end of each turn, produce a structured handoff block using the format below:

```markdown
### Agent Turn Handoff

- **Persona:** `[active persona identifier, e.g., enterprise-architect]`
- **Artifacts Produced:** `[path or paths to the files in the cohesive deliverable]` (Status: `Draft` / `Proposed` /
  `Validated`)
- **Dependencies Evaluated:** `[governing artifacts consulted, e.g., docs/user-stories/US-0102-export-orders-csv.md]`
- **Key Decisions & Findings:** `[concise summary of decisions made or findings uncovered]`
- **Next Required Persona / Action:** `[target persona and concrete action required next]`
```

## Orchestrator responsibilities

The orchestrating agent (the agent coordinating other agents, which may be the human's primary chat agent) must:

- Maintain a **task decomposition plan** visible to the human before execution begins.
- Get human approval of the plan before dispatching subagents.
- Collect and validate the handoff notes from each subagent before passing context forward.
- Detect and surface contradictions between subagent outputs before presenting a combined result.
- Never silently merge conflicting outputs.
- **Hold the git monopoly.** The orchestrator is the **only** agent that commits and merges locally. Subagents hand off
  changes on their own branches; the orchestrator integrates them after resolving contradictions. See
  `.agents/rules/01-execution-isolation.md`. The monopoly is **per machine**; across humans, integration happens only
  through human-reviewed MRs/PRs (see `.agents/protocols/multi-human-collaboration.md`).

## Execution isolation (concurrent agents)

When agents run concurrently, isolate their work so one agent can never disrupt another's:

1. **One worktree per agent.** Each agent works in its own git worktree on its own branch. Agents do not share a working
   tree. Everything is merged afterward by the orchestrator.
2. **One committer.** Only the orchestrator commits and merges locally (the git monopoly above). Subagents produce
   changes in isolation and request a handoff; they do not merge into a shared or integration branch.
3. **No self-approval.** No agent approves the MR or deploy that it (or another agent) produced; approval is a
   human-only act. See `.agents/rules/00-no-self-approval.md`.
4. **Gates before an MR.** An agent may open an MR only after all applicable checks in the **pre-MR gate** pass,
   including the full test suite, regression tests, and static analysis (e.g., Sonar/SAST) when relevant. Where quality
   baselines exist (e.g., extraction accuracy), any negative drift against baseline **aborts** the MR. See the pre-MR
   gate in `.agents/AGENTS.md` → **SSDLC Phase Awareness**.

These rules are extensions in `.agents/rules/` (`00-no-self-approval.md`, `01-execution-isolation.md`);
`.agents/AGENTS.md`
and this protocol remain canonical if they ever conflict.

## Subagent responsibilities

- Accept only the context explicitly handed to them. Do not read the entire conversation history unless instructed.
- Produce only what their persona and the current task require.
- Flag scope creep: if a task requires knowledge outside the current persona's domain, stop and request a handoff.
