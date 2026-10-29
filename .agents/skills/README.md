# Skills (approved procedures)

Skills are **step-by-step procedures** for recurring tasks (drafting an ADR, creating a change record, running a threat
model, etc.). Skills are meant to be followed **exactly**.

## Must-haves

- Each skill must be:
    - **Deterministic** (no "do what you think best" steps)
    - **Gated** (states what approvals/artifacts must exist first)
    - **Auditable** (produces concrete outputs)
    - **Human-friendly** (a human can execute it without AI)

## Approved repository skills

Imported skills follow `.agents/rules/10-imported-assets.md`; their provenance is recorded below.

- **`ponytail`** (`.agents/skills/ponytail/SKILL.md`): Pragmatic senior developer execution mode (YAGNI, standard
  libraries, minimal complete diffs, ceiling annotations).
- **`ponytail-review`** (`.agents/skills/ponytail-review/SKILL.md`): Quality and lean diff review (correctness, safety,
  scale, tests, bloat).
- **`ponytail-audit`** (`.agents/skills/ponytail-audit/SKILL.md`): Repository-wide quality, scalability, and debt audit.
- **`ponytail-debt`** (`.agents/skills/ponytail-debt/SKILL.md`): Harvests shortcut/deferral comments into an auditable
  technical debt ledger.
- **`ponytail-gain`** (`.agents/skills/ponytail-gain/SKILL.md`): Empirical benchmark scoreboard display.
- **`ponytail-help`** (`.agents/skills/ponytail-help/SKILL.md`): Reference card for commands and configurations.
- **`ub-mode`** (`.agents/skills/ub-mode/SKILL.md`): Make it Work (TDD, domain modeling), Make it Right (root causes,
  CRAP / duplication / mutation gates, refactoring), Follow Practice (log, cleanup, small ordered commits).

### Conflicting skills

When two skills cover the same concern (for example, two coding modes), only one is active at a time:

- The persona lists them in order under **Active Skills**. The first is the **recommended** skill; the rest are
  **fallbacks**.
- Use a fallback only when the recommended skill is unavailable (not yet approved on the shared trunk, or the tool
  cannot load it) or the human names the fallback for the task. State which skill is active and why.
- Skills for different concerns (for example, a coding mode and a review skill) do not conflict and may be combined.
- `.agents/AGENTS.md`, `.agents/protocols/`, and `.agents/rules/` override every skill.

### Skill discovery

`.agents/skills/` is the only copy of each skill. Tools load it in one of three ways:

| How                                         | Tools                                                                                                                   |
|---------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| Native: scans `.agents/skills/`             | OpenAI Codex, Gemini CLI, GitHub Copilot, OpenCode, Cursor, Windsurf (now Devin Desktop), Roo Code, Zed (trusted trees) |
| Symlink `.claude/skills` → `.agents/skills` | Claude Code; also Cline, which reads `.claude/skills/`                                                                  |
| Symlink `.kiro/skills` → `.agents/skills`   | Kiro (symlink support is not documented – unverified)                                                                   |
| Manual (no skill discovery found in docs)   | Aider, Sourcegraph Cody, Amazon Q Developer (IDE), JetBrains Junie                                                      |

In the manual case, when the human names a skill (e.g., `/ub-mode`), read `.agents/skills/<name>/SKILL.md` in full and
follow it. On Windows, clone with `git config core.symlinks true` (and Developer Mode or admin rights) so the symlink
works; otherwise Claude Code and Cline fall back to the manual case.

### Provenance of imported skills

| Skill(s)                     | Source                                                                | Version                                  | License                                                    | Imported   |
|------------------------------|-----------------------------------------------------------------------|------------------------------------------|------------------------------------------------------------|------------|
| `ponytail`, `ponytail-*` (6) | [DietrichGebert/ponytail](https://github.com/DietrichGebert/ponytail) | 09c434ab73ca43761c5d8b0c5d341926c2fba710 | MIT                                                        | 2026-10-17 |
| `ub-mode`                    | [unclebob/ub-mode](https://github.com/unclebob/ub-mode)               | 57a8bdd28e45ed41900ace0bafa2ff76dd9ed70f | None yet; expected 0BSD like unclebob/crapper (unverified) | 2026-10-29 |

## Skill file requirements

Every skill file must include:

- **Purpose**
- **Inputs required**
- **Preconditions / governance gates**
- **Procedure** (numbered steps, no skipping or reordering)
- **Outputs** (file paths)
- **Validation** (how to confirm correctness)
- **Escalation points** (where the skill must pause for human input)
- **Sources**

## Template

```markdown
# <Skill name>

## Purpose

## Inputs required

## Preconditions / governance gates

## Procedure (mandatory)

## Outputs

## Validation

## Escalation points

## Sources
```
