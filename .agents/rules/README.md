# Rules (non-negotiable governance)

Rules are **hard constraints** that override all other instructions. If a user request conflicts with a rule, the agent
must surface the conflict and refuse to silently violate the rule.

## Must-haves

- Maintain a small set of **high-signal, testable** rules.
- Each rule file must include:
    - **Scope** (what it applies to)
    - **Rule statements** (“MUST / MUST NOT / SHOULD”)
    - **Rationale** (short)
    - **Examples** (1 good / 1 bad)
    - **Enforcement** (how to detect/confirm)

## Precedence

1. `.agents/AGENTS.md` (canonical agent contract)
2. `.agents/rules/*` (extensions; must not contradict `AGENTS.md`)
3. Vendor/tool-specific instructions (must not contradict the above)

## Rule file conventions

- **Naming**: `00-<topic>.md` for core rules, then `10-...`, `20-...` for extensions.
- **Immutability**: Rules may evolve, but changes must be reviewed like any other governance artifact.
