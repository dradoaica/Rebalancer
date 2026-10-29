# Multi-Human Collaboration Protocol

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

When several humans (and their agents) work on one repository from different machines, each checkout may hold a stale
set of governance artifacts. Git reports only textual conflicts. It does **not** report duplicate identifiers under
different filenames, duplicate artifacts, or contradictory decisions. This protocol covers those gaps.

## Integration authority

- **Local:** each machine's orchestrator holds the git monopoly for its own agents
  (`.agents/protocols/multi-agent-orchestration.md`).
- **Shared:** the **shared trunk** (default branch of the shared remote, e.g., `origin/main`) is the source of truth for
  governance artifacts. Humans integrate only through **human-reviewed MRs/PRs** into it. No orchestrator has authority
  over other human branches or artifacts.

## Binding statuses take effect only on the shared trunk

- A binding status (`Accepted` decision record, threat model, or SDP; `Ready` user story; `Approved` change record)
  takes effect only once merged into the shared trunk via a human-approved MR/PR. Elsewhere, it is a proposal.
- A Tier 1 decision record therefore merges in its **own MR/PR before** the implementation that depends on it;
  implementation branches then rebase onto the shared trunk.
- Agents **MUST NOT** treat an artifact as approved because it is approved on a local or feature branch.

## Collision-free identifiers

- **Before allocating an identifier**, the agent **MUST**:
    1. `git fetch` the shared remote (read-only).
    2. Take the next free number from the **shared trunk** (e.g., `git ls-tree -r --name-only origin/main docs/`), not
       the local checkout.
    3. Check open MRs/PRs for the same number when tooling allows (e.g., `gh pr list`); otherwise say they were not
       checked.
- Use only the naming scheme in each artifact folder's README.
- **Renumber before merge, never after:** on a collision, the artifact not yet on the shared trunk is renumbered
  (filename, `id`, and references on its branch). Identifiers on the shared trunk never change and are never reused.

## Search before drafting

Before drafting, search the shared trunk (and open MRs/PRs, when available) for artifacts with the same scope: similar
titles, the same capability, the same decision concern. If one exists, propose **extending, superseding, or linking**
to it instead of creating a parallel artifact.

## Explicit decision scope

- Every decision record names the one concern it decides in front matter `decides` and in **Context and Problem
  Statement** (e.g., "primary datastore for the orders service"). Threat models use `scope`.
- **Invariant:** at most **one** `Accepted` decision record per concern on the shared trunk. A second record on the
  same concern either supersedes the first (old: `Superseded` + `superseded-by`; new: `supersedes`) or is `Rejected`.

## Approval evidence

- Evidence is the approver's handle in front matter `approved-by` **plus** the same person's approval on the MR/PR
  that merges the artifact into the shared trunk.
- Teams **should** enforce this with path-scoped required reviewers where the platform supports it (e.g.,
  `CODEOWNERS` on GitHub/GitLab, required reviewers by path on Azure DevOps); otherwise the reviewer verifies it.
- Agents never fill in `approved-by` on their own initiative; they may only record a human's explicit, in-session
  approval, naming that human. They report binding statuses that lack it.

## Claim work early

Claim a story **when work starts**, not when it ends: assign it in the issue tracker, or open a draft MR/PR that sets
its `owner` and `In progress` status. Before starting, check both (and the shared trunk); if the story is already
claimed by another owner, stop and tell the human.

## Governance reconciliation (after every sync)

After every merge or rebase from the shared trunk, and before opening an MR/PR that touches `docs/`, the agent **MUST**
run these checks over all artifacts' front matter and report the results. Run `python3 -I scripts/govern_check.py
--base <shared trunk>` first: it covers check 1, the exact-match half of 3, check 4, check 5, and the front-matter half
of 8 (`.agents/rules/02-governance-checks.md`). Do the rest by hand.

| # | Check                 | Finding                                                                                           |
|---|-----------------------|---------------------------------------------------------------------------------------------------|
| 1 | Duplicate identifiers | Two artifacts share an `id`, or a filename does not match its `id`                                |
| 2 | Duplicate artifacts   | Two artifacts cover the same story, service, threat scope, or decision concern                    |
| 3 | Contradictory records | Two `Accepted` records decide the same concern, or two artifacts state incompatible rules         |
| 4 | Broken references     | `depends-on` cites a missing or renumbered identifier, or `Sources` cites one not in `depends-on` |
| 5 | Stale premises        | A `Proposed`/`Ready`/`In progress` artifact depends on a `Rejected`/`Deprecated`/`Superseded` one |
| 6 | Status conflicts      | Branches applied different status transitions to the same artifact                                |
| 7 | Ownership conflicts   | One story is claimed or `In progress` under different owners                                      |
| 8 | Approval evidence     | A binding status lacks `approved-by`, or it does not match the MR/PR approver                     |

Report each finding with file paths, check number, and resolution options (supersede, reject, merge and cancel the
other with a link, renumber, fix references).

**Resolution rules:**

- The agent may propose mechanical fixes (renumbering, reference updates) as a diff, only on its **own unmerged
  branch**.
- Checks 2, 3, 5, 6, 7, and 8 are resolved **only by humans**. The agent does not pick a winner, merge records, or
  change the status of an artifact it did not author. If owners differ, it names both and asks them to decide together.
- Resolutions that change approved artifacts follow the normal gates; accepted records stay immutable.
- Until resolved, affected artifacts are **not** valid sources; block dependent work and say so.

## Textual merge conflicts

- In an **approved** artifact, never resolve a conflict with "ours"/"theirs" without human review: two humans changed a
  governed record.
- In a **draft**, the agent may propose a resolution showing both sides and the result.
- Do not hand-maintain shared index files (e.g., a decision log); derive them from the artifacts.
