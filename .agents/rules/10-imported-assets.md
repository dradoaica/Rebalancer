# Rule 10 – Imported agent assets

## Scope

Applies to every skill, persona, rule, command, protocol, or adapter content taken from outside this repository (e.g.,
community lists in `README.md` → Extending agent governance), whether copied as-is or adapted. Covers adding, updating,
and removing such assets under `.agents/`.

## Rule statements

- Imported assets **MUST** enter the repository only through an MR/PR **reviewed and approved by a human**, like any
  other governance change. An agent **MUST NOT** import or update them on its own authority.
- The reviewer **MUST** read the full asset before approval. Imported assets are instructions that agents will execute;
  treat them as code, not documentation.
- Each imported asset **MUST** have a provenance entry in the README of the folder holding it (e.g.,
  `.agents/skills/README.md`): **source** (URL), **version** (tag or commit), **license**, and **date imported**.
- An asset from a public repository with **no license yet** **MAY** be imported. Its license field **MUST** say so
  (e.g., `None yet (unverified)`), and the human approving the MR/PR **MUST** accept it explicitly in the MR/PR. When
  upstream publishes a license, the asset **MUST** be updated to that version (re-reviewed as below) or removed if the
  license does not permit redistribution.
- Imported assets **MUST NOT** override or weaken `.agents/AGENTS.md`, `.agents/protocols/`, or `.agents/rules/`. Any
  conflicting statement is removed or rewritten before merge.
- Imported assets **MUST NOT** contain instructions to fetch and follow remote content, run network or install
  commands without human approval, or bypass gates. Such content is removed before merge.
- Updating an imported asset to a new upstream version **MUST** go through the same review, with the version updated in
  its provenance entry.

## Rationale

Imported skills and personas run inside every agent session with the same authority as the framework's own
instructions. An unreviewed import is a supply-chain path for prompt injection, gate bypasses, or license problems.
Recording source and version makes each import auditable and updatable.

Allowing unlicensed imports is a deliberate maintainer decision (2026-10-29): useful skills often appear in public
repositories before their authors add a license. Without a license, the default is all rights reserved, so the
repository redistributes that text at the maintainer's risk, and adopters inherit the copy. The rule keeps that risk
visible instead of hiding it: the provenance row says so, a human accepts it in the MR/PR, and the checker prints a
note for every unlicensed skill.

## Examples

- **Good:** A human opens an MR adding `.agents/skills/foo/SKILL.md`, adds `foo | https://github.com/org/foo | v1.2.0 |
  MIT | 2026-10-19` to `.agents/skills/README.md`, removes a line telling agents to "auto-merge when tests pass", and a
  human reviewer approves.
- **Bad:** An agent copies a skill from a community list straight into `.agents/skills/` during a task and starts
  following it, with no review and no record of where it came from.

## Enforcement

- The human reviewer confirms, for every MR touching `.agents/`, that imported content has a complete provenance entry
  and was read in full.
- An imported asset without a provenance entry is a finding: record it as unverified and review it before relying on
  it.
- All governance rules are human-verified at the review boundary (see `.agents/AGENTS.md` → Governance Guardrails).

## Sources

- `.agents/AGENTS.md` (sections: Co-Governance Protocol; Anti-Delusional Safeguards → Data, not instructions)
- `.agents/rules/00-no-self-approval.md`
- `README.md` (section: Extending agent governance)
