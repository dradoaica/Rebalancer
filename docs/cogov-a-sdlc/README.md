# Co-Governed Agentic SDLC (Software Development Life Cycle)

A spec-first framework for human and agent co-governance across SSDLC using DevSecOps, Agile, TOGAF, and ITIL
practices. The goal is long-term maintainability and clearer human–agent collaboration.

## Start here

The **single canonical entry point** for AI agents and for humans using agents in this repository is:

- [`.agents/AGENTS.md`](../../.agents/AGENTS.md)

If you are configuring a tool or vendor (Copilot, Claude, OpenAI, Gemini, Codex, OpenCode, and similar), use its
adapter file (for example [`.github/copilot-instructions.md`](../../.github/copilot-instructions.md), root
[`AGENTS.md`](../../AGENTS.md), or [`GEMINI.md`](../../GEMINI.md)), which defers to `.agents/AGENTS.md`.

To begin product and specification work, use [`docs/initial-product-specs/README.md`](../initial-product-specs/README.md).

## Single-repository setups

If the product lives in **one repository**, keep the framework **in the same repo** as the codebase. Use
`.agents/AGENTS.md` as the canonical agent entry point and store governance artifacts under `docs/` as described below.

## Multi-repository setups

If you apply the framework across **several repositories**, a common approach is to keep **this** repository as the
**parent** and add other repos as **Git submodules**. That preserves one canonical `.agents/` contract and traceability
while each codebase keeps its own history and remotes.

Git records submodules in **`.gitmodules`** at the repository root. That file is created or updated when you run
`git submodule add <url> <path>`. Example:

```gitconfig
[submodule "repos/api"]
	path = repos/api
	url = https://github.com/your-org/your-api.git
[submodule "repos/web"]
	path = repos/web
	url = https://github.com/your-org/your-web.git
```

## Repository layout (governance artifacts)

| Area | Location |
|------|----------|
| Agent governance | `.agents/` (personas, rules, skills, commands, tools, and related assets) |
| Decision records (ADRs) | `docs/adrs/` |
| User stories | `docs/user-stories/` |
| Service design packages (SDPs) | `docs/sdps/` |
| Change records | `docs/change-records/` |
| Problem records | `docs/problem-records/` |

## Extending agent governance

You can add personas, rules, skills, commands, tools, and similar assets from community lists, for example:

- [github/awesome-copilot](https://github.com/github/awesome-copilot)
- [affaan-m/everything-claude-code](https://github.com/affaan-m/everything-claude-code)
- [wshobson/agents](https://github.com/wshobson/agents)
- [hesreallyhim/awesome-claude-code](https://github.com/hesreallyhim/awesome-claude-code)
- [PatrickJS/awesome-cursorrules](https://github.com/PatrickJS/awesome-cursorrules)

Imports must not override `.agents/AGENTS.md`; check license and policy fit before adopting third-party content.
