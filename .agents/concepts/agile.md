# Agile (in co-governed agentic SDLC)

Agile here means **small batches of value**, **fast feedback**, and **explicit priorities**—while staying aligned with
SSDLC gates, decision records, and human approval. Agents assist; they do not replace product ownership or governance.

## User stories

User stories are maintained in [docs/user-stories](../../docs/user-stories).

- Agents **draft** new stories (typically under the **agile-user-story-writer** persona in `.agents/agents/`); humans
  **review and approve** before work is treated as committed backlog scope where your process requires it.
- Use the template [`user-story-template.md`](../../docs/user-stories/user-story-template.md) for new stories.
- Stories **must** be traceable: link to decision records, SDPs, or change records when the work implies them.
- Prefer **vertical slices** (end-to-end value) over horizontal “layer only” tasks unless a decision record authorizes a
  platform change.

## Backlog and flow

- **Product backlog:** ordered list of stories and enablers; top items are small enough to complete in an iteration.
- **Refinement:** clarify acceptance criteria, dependencies, and risks *before* iteration planning—not during “coding
  only.”
- **Iteration (sprint) backlog:** subset the team commits to for the iteration; scope changes go through the product
  owner and governance (e.g., change record if production risk).

Naming and tooling (Jira, GitHub Projects, etc.) are **team-specific**; this repo standardizes **where markdown
artifacts live** under `docs/user-stories/`.

## Ceremonies (typical)

Adapt names and cadence to your organization; keep the **intent**:

| Ceremony           | Intent                                                 |
|--------------------|--------------------------------------------------------|
| Iteration planning | Align on goal, pull work, surface dependencies/gates   |
| Daily sync         | Unblock progress; no status theater                    |
| Review / demo      | Validate outcomes against acceptance criteria          |
| Retrospective      | Improve process; feed into problem records if systemic |

## Definition of Ready (DoR) — minimum before “ready to build”

- Story has clear **As a / I want / So that** (or equivalent) and **acceptance criteria**.
- Dependencies and **SSDLC phase** are identified (Planning vs Design vs Coding, etc.).
- If the work implies a **durable technical choice**, a **decision record** exists or is in flight.
- Security/privacy notes are addressed or explicitly “N/A” with rationale.

## Definition of Done (DoD) — minimum before “done”

- Acceptance criteria met; tests/checks agreed with the team are satisfied.
- No silent changes to public APIs, schemas, or config without the artifacts required by `.agents/AGENTS.md`.
- Traceability preserved: story links to **change record** (or equivalent) when released to shared environments.

## Relationship to other frameworks

- **SSDLC** (`ssdlc.md`): security and quality gates across phases; Agile does not skip them.
- **Decision records** (`togaf/architectural-decision-records.md`): significant design choices before implementation.
- **ITIL** (`itil/*`): operational change and problem management for production-impacting work.

## Documentation

- [Agile Manifesto](https://agilemanifesto.org/)
- [Scrum Guide](https://scrumguides.org/) (if using Scrum)
