# Tools (authorization and safe usage)

This folder defines the **approved tool surface area** for agents in this repository, including constraints and
required permissions. If a tool is not documented here, the agent must treat it as **not approved** unless the human
explicitly authorizes it in the current session.

## Must-haves

- A single allow-list of tools agents may use
- Per-tool constraints:
    - allowed operations
    - forbidden operations
    - required human approvals
    - logging/audit expectations (what to report back)
- Environment-specific restrictions (local-only vs remote)

## Files in this folder

- `allowed-tools.md`: the allow-list and constraints (authoritative)
