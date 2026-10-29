# Rule 02 – Executable governance checks

## Scope

Applies to every AI agent in this repository, and to every human reviewer, for the governance rules a machine can
verify. Covers `scripts/govern_check.py`, its tests in `scripts/tests/`, and anything that runs it (agents, harness
hooks, CI).

## Rule statements

- An agent **MUST** run `python3 -I scripts/govern_check.py` before opening an MR/PR (with `--base <shared trunk>`
  when the diff touches `docs/`) and after every sync from the shared trunk, and **MUST** report its output.
- Any finding, and any exit code `2` (usage or parse error), **MUST** block the MR/PR until it is fixed or a human
  accepts it as `N/A` with a rationale.
- An agent **MUST NOT** change the checker, its tests, or a template in the same change that produced the finding
  it silences. A change to the checker goes in a separate, reviewed change.
- An agent **MUST NOT** present a clean result as approval, or as covering anything outside the table below; it
  reports the checker's "Not checked" line with the result.
- Every check **MUST** mirror a text rule in `.agents/` or `docs/*/README.md`. The text stays canonical: if the
  checker and a text rule disagree, the checker is the defect. Hooks and CI **MUST** call this script, not
  re-implement its checks.
- The checker **MUST** stay read-only and dependency-free: Python 3.13+ standard library only, no file writes, no
  network, and no subprocess other than read-only `git` (only with `--base`).
- If Python 3.13+ is unavailable, the agent **MUST** perform the checks below by hand and record the checker as `N/A`
  with a rationale for human acceptance.

### Checks

The schema (keys, list-typed keys, statuses) is read from each folder's templates (files named `<type>-template*.md`);
templates and READMEs are not checked. Tests in `scripts/tests/` fail if the checker's hardcoded statuses or naming
patterns drift from the templates and READMEs. Front matter must use the flat subset the templates use (`key: value`,
empty values, quoted strings, inline `[ ]` lists, `#` comments); anything else is `GOV-00`, never a
guess.

| ID     | Check                                                                                                                                                                                                                                              | Mirrors                                            |
|--------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------|
| GOV-00 | Front matter or a template cannot be parsed, or a file is not UTF-8                                                                                                                                                                                | Each folder README → Metadata                      |
| GOV-01 | Front matter present, with every template key in the template's shape (single value or list)                                                                                                                                                       | Each folder README → Metadata ("keep every key")   |
| GOV-02 | `status` is one of the template's allowed values                                                                                                                                                                                                   | Each folder README → Statuses                      |
| GOV-03 | The file name follows the folder's naming scheme and matches `id`                                                                                                                                                                                  | Each folder README → Naming                        |
| GOV-04 | No two artifacts share an `id`                                                                                                                                                                                                                     | Reconciliation check 1                             |
| GOV-05 | Every identifier in `depends-on`, `supersedes`, and `superseded-by` exists                                                                                                                                                                         | Reconciliation check 4                             |
| GOV-06 | Every repository path cited in `Sources` exists; `Sources` cites no artifact missing from `depends-on`                                                                                                                                             | `.agents/AGENTS.md` → Required traceability format |
| GOV-07 | A binding status, or any status that can only follow it, has a non-empty `approved-by`                                                                                                                                                             | Reconciliation check 8 (front-matter half)         |
| GOV-08 | A `Proposed`, `Ready`, or `In progress` artifact does not depend on a `Rejected`/`Deprecated`/`Superseded` one                                                                                                                                     | Reconciliation check 5                             |
| GOV-09 | At most one `Accepted` ADR per `decides` value, and one `Accepted` threat model per `scope` value                                                                                                                                                  | Reconciliation check 3 (exact-match half)          |
| GOV-10 | With `--base`: an approved ADR or change record changed only `status`, `date`, `approved-by`, `superseded-by` (a change record's Post-implementation section may only grow: its earlier lines stay, in order; template placeholders may be filled) | `.agents/AGENTS.md` → Immutable audit trail        |
| GOV-11 | Every skill in `.agents/skills/` has a provenance row with source, version, license, and date; an unlicensed skill is printed as a note, not a finding                                                                                             | `.agents/rules/10-imported-assets.md`              |

**Not checked (human review):** tier correctness, duplicate artifacts by meaning (reconciliation 2), status and
ownership conflicts across branches (6, 7), whether `approved-by` matches the MR/PR approver (second half of 8), and
content quality.

## Rationale

Every other gate is text that agents read and a human verifies. With several agents, personas, and models, that does
not scale: agents can cite artifacts that do not exist, smaller models produce the required format while skipping the
reconciliation checks, and every mechanical check lands on the reviewer until approval becomes cosmetic (see Rule 00
→ Rationale). A check that runs the same way for every model removes the bookkeeping from human review, so reviewers
spend their time on judgment.

Options considered: a **standard-library Python script** (chosen: it parses front matter strictly and cross-references
artifacts, with no supply chain beyond the interpreter); a **POSIX shell script** (cross-file checks become fragile
text processing); **third-party linters or hook managers** (each is a new dependency, Tier 1 for the framework and
every adopter, and none knows these cross-artifact rules); **harness-only enforcement** such as Claude Code hooks (one
vendor only, and it would hide the rules in that vendor's configuration); and the **status quo** (the problem itself).

This is a rule rather than a decision record because it governs the framework itself: `docs/` holds templates and
records for adopters, and a framework record there would be copied into every adopting repository and collide with
its numbering.

## Examples

- **Good:** Before opening an MR that adds `docs/user-stories/US-0004-export.md`, the agent runs the checker with
  `--base origin/main`, gets `GOV-06 Sources cites ADR-0003, which is missing from depends-on`, adds `ADR-0003` to
  `depends-on`, reruns until clean, and puts the output and its "Not checked" line in the MR description.
- **Bad:** The checker reports `GOV-07` on an `Accepted` ADR with an empty `approved-by`; in the same branch the agent
  removes `Accepted` from the statuses the checker treats as binding, and reports "governance check green".

## Enforcement

- The human reviewer confirms the MR/PR shows the checker output, and that every finding was fixed or accepted as
  `N/A` by a human.
- Changes to `scripts/govern_check.py` or `scripts/tests/` arrive in their own MR/PR, with
  `python3 -I -m unittest discover -s scripts/tests` passing.
- CI may run the checker and be made required through branch protection; adding CI actions is a dependency change
  with its own review.
- All governance rules are human-verified at the review boundary (see `.agents/AGENTS.md` → Governance Guardrails).

## Sources

- `.agents/AGENTS.md` (sections: Pre-MR gate; Anti-Delusional Safeguards → Immutable audit trail; Governance
  Guardrails)
- `.agents/protocols/multi-human-collaboration.md` (section: Governance reconciliation)
- `.agents/rules/00-no-self-approval.md`, `.agents/rules/10-imported-assets.md`
- `.agents/rules/README.md` (precedence and format)
