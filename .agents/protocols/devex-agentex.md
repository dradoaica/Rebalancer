# Developer Experience (DevEx) and Agent Experience (AgentEx)

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

A well-governed agentic SDLC must be usable by both humans and agents. Design every artifact with both audiences in
mind. Read the full [Agent Experience (AgentEx) & Context Management](../concepts/agent-experience.md) guide.

## DevEx principles (for human developers)

- Every agent output must be human-readable and human-reviewable without specialized tooling.
- No agent output should require another agent to interpret it.
- Pipeline stages, IaC, and CaC must be written so that a developer who has never used AI tooling can understand,
  modify, and operate them.
- Prefer explicit over implicit. Prefer verbose over clever.

## AgentEx principles (for AI agents)

- Every task handed to an agent must have: a clearly scoped acceptance criterion, a pointer to the relevant spec, and an
  explicit list of constraints (what the agent must not do).
- Context windows are finite. Keep individual task files small and self-contained. Do not require agents to read the
  entire codebase to understand a single task.
- Avoid ambiguous pronouns and forward references in specs and instructions. Write as if the reader has no memory of
  previous conversations.
- Prefer structured formats (tables, numbered lists, YAML) over prose for information that agents must parse.
- Autonomous implementation tasks should include automated tests when possible. This requirement does not exclude
  research, review, or drafting tasks. If an implementation cannot be tested automatically, define explicit manual
  acceptance criteria and require human verification before considering it complete.
