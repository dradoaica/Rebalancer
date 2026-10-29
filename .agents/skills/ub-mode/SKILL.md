---
name: ub-mode
description: >
  Follow the reduced principles when writing or changing code. Every change
  follows First Make it Work, then Then Make it Right, then Follow Practice.
  Use when the user runs /ub-mode or asks for the reduced principles.
---

# Skill: ub-mode (Make it Work, Make it Right, Follow Practice)

## Purpose

Applies a disciplined engineering loop to every code change: **First Make it Work** (laziness, foundational thinking,
TDD, first-principles redesign, domain modeling), **Then Make it Right** (root causes, verifiable units, behavior tests,
complexity / duplication / mutation gates, refactoring), and **Follow Practice** (log, context hygiene, cleanup,
small ordered commits).

Principle text is kept close to upstream. Lines marked **cogov:** adapt upstream to this repository's governance and
take precedence over the upstream wording next to them.

## Inputs required

- Governing artifact for the change, per the change-tier matrix in `.agents/AGENTS.md`: accepted ADR (`docs/adrs/`),
  `Ready` user story (`docs/user-stories/`), or an existing story/issue reference for Tier 3.
- Active codebase, callers, callees, and test fixtures.
- The project's test runner and, if present in the codebase, the quality-gate tools (crapper, dryer, mutator, or
  equivalents).

## Preconditions / governance gates

- **Governance alignment:** `.agents/AGENTS.md` and `.agents/protocols/` win over anything in this skill. State the
  change tier and SSDLC phase before starting.
- **Tier 1 gate:** ub-mode cannot bypass the **Accepted** ADR requirement. Redesigns, domain/type remodeling, or new
  tooling that touch a "Tier 1 by default" area (dependencies, schemas, public APIs, configuration schemas, security
  controls, topology) stop at an ADR draft.
- **Rule 00:** follows `.agents/rules/00-no-self-approval.md` – the agent never approves or merges its own change.
- **Rule 01:** follows `.agents/rules/01-execution-isolation.md` when more than one agent is involved.
- **Maintainability:** follows `.agents/protocols/maintainability.md` (regression statement, contract tests, debt
  governance).

## Procedure (mandatory)

Every time you are asked to make a change follow, in order:

1. First Make it Work.
2. Then Make it Right.
3. Follow Practice.

### 1. First Make it Work

#### Laziness Protocol

- One decision lives in one place.
- Ship the smallest edit that solves the problem.

#### Foundational Thinking

- Get the data shape right before you write the logic.
- Do first whatever helps every later phase.
- If a concurrent writer would matter, do not share state.
- **cogov:** A data shape that changes a persisted schema, migration, or public contract is Tier 1 – draft the ADR
  first.

#### TDD

- Write the failing test before you change the code.
- See it fail for the reason you expect.
- Make the smallest change that makes it pass.
- If an honest test is impractical, do not write a bad one.
- **cogov:** When no honest automated test is possible, write explicit manual acceptance criteria and require human
  verification before calling the change complete (`.agents/protocols/devex-agentex.md`).

#### Redesign From First Principles

- Do not bolt a new requirement onto the old design.
- Ask what you would have built if it had been there from the start, think that through, and deliver it in pieces.
- **cogov:** A redesign is not authorized by the bug or story that prompted it. If the redesign is Tier 1, draft a
  `Proposed` ADR and stop. If it exceeds the governing story's scope, propose it to the human (or draft a story)
  instead of implementing it – no gold-plating (`.agents/AGENTS.md` → Anti-Delusional Safeguards).

#### Attack the Premise

- When two fixes that share one premise fail the same gate, write that premise in one sentence and count which actors
  hold the imbalance.
- Remove the assignment.
- Do not start another fix until you have done that.
- **cogov:** If the premise comes from a governance artifact (ADR, story, threat model), do not discard it – surface it
  to the human with the evidence and, if needed, draft a superseding artifact.

#### Subtract Before You Add

- Take complexity out before you add anything.

#### Outcome-Oriented Execution

- Aim at the end state you can check.
- A break in the middle is allowed when you planned it and can undo it.
- **cogov:** An intermediate break lives only on your own branch/worktree. Nothing broken reaches a shared branch, and
  the pre-MR gate still applies in full.

#### Build the Lever

- If the work is not obvious at a glance, build the tool that does it.
- Do one unit by hand, then make the tool match that unit.
- **cogov:** A lever is a local, task-scoped script. A lever that adds a dependency, becomes a pipeline stage, or
  persists in the repository is a change in its own right – apply its tier (and Tier 1 for dependency manifests).

#### Architecture

##### Model the Domain

- Put the domain in a structure that makes the illegal states impossible.
- Maintain a glossary of domain terms.
- If the code is already clear and local, leave it.
- **cogov:** Update the repository's existing glossary if there is one. If none exists, propose its location to the
  human instead of inventing one.

##### Boundary Discipline

- Check the data where it enters.
- Inside, trust it.
- Keep the business logic in a pure function.

##### Type System Discipline

- Make the illegal value impossible to write.
- Parse outside data once, where it comes in.
- Do not silence the compiler.
- **cogov:** If a suppression is truly unavoidable, it is technical debt: annotate it with reason, risk, and a tracking
  item (`.agents/protocols/maintainability.md` → Technical debt governance).

##### Make Operations Idempotent

- Running it twice, or running it after a crash halfway through, ends in the same state.

##### Separate Before Serializing Shared State

- If they do not need the same mutable thing, give each its own.
- If they do, allow one writer, and enforce that in the structure.

### 2. Then Make it Right

#### Fix Root Causes

- Reproduce it.
- Fix the cause.
- A guard that only hides the crash is not a fix.
- **cogov:** Recurring failures or an unknown root cause call for a problem record in `docs/problem-records/`.

#### Sequence work into verifiable units

- Finish one small unit and check it.
- Then start the next.

#### Test Behavior, Not Implementation

- The test calls the code the way its user does and checks the result that user would see.
- If the test would still pass when every function returned nothing, throw it out.

#### Quality-gate tools (Crapper, Dryer, Mutator)

- **cogov (applies to all three):** Use a tool only if it, or an equivalent, is already present in the codebase or the
  human authorizes it in-session (`.agents/tools/allowed-tools.md`). Do not install or fetch it without human approval
  (`.agents/rules/10-imported-assets.md`); adding it to a dependency manifest is Tier 1. If no tool is available, record
  the gate as `N/A` with a rationale and get human acceptance. Treat the thresholds below as a baseline: negative drift
  aborts the MR (pre-MR gate, `.agents/AGENTS.md`).

##### Crapper

- Use the crapper tool.
- Reduce each function under CRAP 10 by splitting it, by testing it, or both.
- Consider whether case statements are worth reducing.

##### Dryer

- Use the dryer tool to eliminate duplication that ranks 0.8 or above.

##### Mutator

- Use the mutator tool.
- Cover the uncovered.
- Kill the survivors.
- Delete dead code.
- **cogov:** Deleting code inside a file in scope is fine. Deleting or renaming a whole repository file needs explicit
  human approval (`.agents/AGENTS.md` → Co-Governance Protocol).

#### Refactoring

- Pin what the code does.
- Change the structure.
- Prove the behavior did not change.
- **cogov:** A refactor is Tier 3: cite the existing story/issue and keep a regression test. A refactor that changes a
  public API, exported interface, or configuration schema is Tier 1.

### 3. Follow Practice

#### Keep a Log

- Keep a log of every action you take and the reason you took it.
- Report that log upon completion.
- **cogov:** The completion report includes the regression statement (which existing behaviors could be affected and
  why they are not broken), the `### Sources` block, and – in multi-agent work – the Agent Turn Handoff block
  (`.agents/protocols/multi-agent-orchestration.md`).

#### Guard the Context Window

- Keep the main thread small.
- Send the bulk elsewhere, and keep the summary.
- **cogov:** Delegating to subagents follows `.agents/protocols/multi-agent-orchestration.md`: a decomposition plan the
  human approves first, one worktree per agent, and handoffs the orchestrator validates.

#### Worktree Cleanup

- Find what is safe to remove, confirm nothing still in use needs it, then reclaim the disk.
- Uncommitted work waits for a decision.
- **cogov:** Present the removal list to the human and remove only after explicit confirmation. No destructive commands
  without confirmation (`.agents/tools/allowed-tools.md`).

#### Commit and Push

- Commit often, in small ordered commits.
- Each commit says why, what changed, and how you proved it.
- **cogov:** Each commit also states the change tier and cites the governing artifact. Under concurrency only the
  orchestrator commits (`.agents/rules/01-execution-isolation.md`).
- **cogov (replaces upstream "Push to the main line at the end"):** Never push to `main` or any shared branch on your
  own
  authority. When the work is done, pass the pre-MR gate, then ask the human for approval to push and open an MR/PR. A
  human reviews and merges it (`.agents/rules/00-no-self-approval.md`).

## Outputs

- Code diff with tests written test-first (or manual acceptance criteria where an honest test is impractical).
- Quality-gate results (crapper, dryer, mutator) or `N/A` entries with rationale, pending human acceptance.
- Small ordered local commits stating why, what, how proven, tier, and governing artifact.
- Completion report: action log, regression statement, `### Sources` block (plus handoff block in multi-agent work).
- Drafted governance artifacts when a gate was hit (ADR in `docs/adrs/`, story in `docs/user-stories/`, problem
  record in `docs/problem-records/`), with status `Proposed` / `Draft`.

## Validation

- Every new or changed behavior has a test that failed first for the expected reason, then passed.
- The full test suite and regression tests are green.
- Quality-gate thresholds are met against baseline, or their `N/A` is recorded.
- The diff matches the stated tier; no Tier 1 area is touched without an `Accepted` ADR cited.
- Nothing was pushed, merged, or deleted without explicit human approval.

## Escalation points

- A redesign, domain remodel, or new lever touches a Tier 1 area → draft the ADR and stop.
- A redesign exceeds the governing story's scope → propose it to the human.
- A premise under attack comes from a governance artifact → surface it to the human.
- A quality-gate tool is missing or would need installing → ask the human; record `N/A` only with their acceptance.
- Worktree/file cleanup or uncommitted work found → wait for the human's decision.
- Work is complete → ask for approval to push and open the MR/PR.

## Sources

- Upstream: [unclebob/ub-mode](https://github.com/unclebob/ub-mode) @ `57a8bdd28e45ed41900ace0bafa2ff76dd9ed70f`
  (companion tools: `unclebob/crapper`, `unclebob/dryer`, `unclebob/mutator`)
- Repository Governance: `.agents/AGENTS.md`
- Protocols: `.agents/protocols/maintainability.md`, `.agents/protocols/multi-agent-orchestration.md`,
  `.agents/protocols/devex-agentex.md`
- Rules: `.agents/rules/00-no-self-approval.md`, `.agents/rules/01-execution-isolation.md`,
  `.agents/rules/10-imported-assets.md`
- Tools: `.agents/tools/allowed-tools.md`
