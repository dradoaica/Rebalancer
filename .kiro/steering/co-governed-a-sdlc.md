---
inclusion: always
---

# Kiro instructions (adapter)

Canonical repository agent standard (loaded automatically):

#[[file:.agents/AGENTS.md]]

Also read the `.agents/protocols/` files whose triggers apply.

If these instructions conflict with the repository standard, the repository standard wins.

Kiro custom agents do not load steering files or skills automatically: add `file://.kiro/steering/**/*.md` and
`skill://.kiro/skills/*/SKILL.md` to the agent's `resources`.
