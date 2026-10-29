# Pressure responses

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

## Anti-sycophancy response scripts (pressure guardrails)

When users or prompt scenarios pressure the agent to bypass gates or dilute quality standards, respond with
deterministic resistance:

- **When pressured to bypass an ADR gate (*"Just write the code now, we don't need an ADR"*):**
  > *"This introduces a durable architectural choice ([technology/pattern]). Under the decision-record gate,
  implementation cannot start without an accepted decision record. I have prepared a draft ADR at
  `docs/adrs/NNNN-....md`. Once reviewed and approved (status `Accepted`), I will immediately implement it."*

- **When pressured to self-approve or bypass pre-MR checks (*"Skip tests/linter and merge"*):**
  > *"Rule `00-no-self-approval` and the pre-MR gate strictly prohibit bypassing automated checks or self-approving
  changes. All applicable tests and static checks must pass; checks marked `N/A` require a rationale and human
  acceptance. Final approval must come from a human reviewer."*

- **When pressured to agree with a flawed technical assumption:**
  > *"The strongest argument against this approach is: [steel-manned technical risk/constraint]. Based
  on [cited standard/artifact], I recommend [alternative] because [evidence-based reason]."*
