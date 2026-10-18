# Rule 00 – No self-approval

## Scope

Applies to every AI agent in this repository, including an orchestrator and every subagent it spawns. Covers approval of
merge requests / pull requests, code reviews, release gates, decision-record acceptance, and deployments.

## Rule statements

- An agent **MUST NOT** approve, mark as reviewed, or merge any change, MR/PR, or review that it produced.
- An agent **MUST NOT** approve another agent's output as a substitute for human review where a gate requires human
  approval. Agent-to-agent review may *inform* a human decision; it never *replaces* it.
- An agent **MUST NOT** set a decision record to **Accepted**, or trigger a deployment, on its own authority (see
  `.agents/AGENTS.md` → Co-Governance Protocol and Decision-record gate).
- An agent **MUST** stop at the approval boundary and hand off to a human with a clear summary of what needs approving
  and why.

## Rationale

Approval is the point where human-in-the-loop governance is actually exercised. If an agent can approve its own (or a
peer agent's) work, the gate is cosmetic, and the framework's anti-delusional and anti-sycophancy guarantee collapse —
an agent would be validating its own priors with no external check. This is a MUST NOT, not a SHOULD: self-approval
defeats the purpose of a gate rather than merely weakening it.

## Examples

- **Good:** Orchestrator finishes assembling subagent work, runs the pre-MR gate, then: "All gates pass. This MR is
  ready for **human** approval; I will not approve or merge it myself. Summary: …"
- **Bad:** Subagent B reviews subagent A's branch, comments "LGTM, approving," and the orchestrator merges on that
  basis with no human in the loop.

## Enforcement

- Human reviewer confirms that every merge/accept/deploy carries a human approver, not an agent, at the MR boundary.
- Any approval action in an agent transcript that lacks a corresponding human decision is a rule violation, and the
  change must be reopened for human review.
- All governance rules are human-verified at the review boundary (see `.agents/AGENTS.md` → Governance Guardrails).

## Sources

- `.agents/AGENTS.md` (sections: Co-Governance Protocol; Decision-record gate; Anti-Sycophancy Protocol)
- `.agents/rules/README.md` (precedence and format)
- Framework improvement incorporating external multi-agent practitioner feedback (2026-10-14).
