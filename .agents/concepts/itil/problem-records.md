# Problem Records

Problem records track the lifecycle of all problems. A problem is the cause of one or more incidents.

## Agent Learning Loop

Problem Records serve as a primary trigger for the **Agent Learning Loop**. When a systemic issue or recurring agent
failure is identified, the Root Cause Analysis (RCA) must evaluate if the failure was due to:

1. **Missing or Ambiguous Rules**: Is there a rule in `.agents/rules/` that should have prevented this?
2. **Skill Deficiency**: Does the agent lack a specific procedure or knowledge in `.agents/skills/`?
3. **Context Pollution**: Was the agent overwhelmed by irrelevant information?

### Feedback Mechanism

Every Problem Record with a root cause related to agent behavior **MUST** result in at least one of the following:

- A new or updated rule in `.agents/rules/`.
- A new or updated skill in `.agents/skills/`.
- A refinement of the agent's persona in `.agents/agents/`.

This ensures the agentic system evolves and learns from its mistakes, moving from mitigation to permanent prevention
through codified governance.

## Reference

Problem records are maintained in [docs/problem-records](../../../docs/problem-records).
