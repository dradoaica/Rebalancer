# Co-Governed Agentic SDLC (Software Development Life Cycle)

> 🚧 **This project is currently under construction. Stay tuned for updates!** 🚀

A spec‑first framework for human and agent co‑governance across SSDLC using the DevSecOps, Agile, TOGAF, and ITIL
frameworks to ensure long‑term maintainability and to reduce AI‑induced delusional spiraling.

## Start here

The **single canonical entrypoint** for AI agents and humans using agents in this repository is:

- `.agents/AGENTS.md`

If you are configuring a tool/vendor (Copilot, Claude, OpenAI, Gemini, Codex, OpenCode, etc.), use its adapter file
(e.g., `.github/copilot-instructions.md`, root `AGENTS.md`, or `GEMINI.md`) which defers to `.agents/AGENTS.md`.

Use the `docs/initial-product-specs/README.md` to begin.

## Single-repository setups

If your project lives in **one repository**, keep the framework **in the same repo** as the codebase (this repository
itself becomes the product repo). Use `.agents/AGENTS.md` as the canonical agent entrypoint and store governance
artifacts under `docs/` as laid out below.

## Multi-repository setups

If you use the framework across **several repositories**, the recommended approach is to keep **this** repository as the
**parent**, and add the other repos as **Git submodules**. That preserves one canonical `.agents/` contract and
traceability while each codebase keeps its own history and remotes.

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

- **Agents governance**: `.agents/` (personas, rules, skills, commands, tools, etc.)
- **Decision records (ADRs)**: `docs/adrs/`
- **User stories**: `docs/user-stories/`
- **Service design packages (SDPs)**: `docs/sdps/`
- **Threat models**: `docs/threat-models/`
- **Change records**: `docs/change-records/`
- **Problem records**: `docs/problem-records/`

## Extending agent governance

Add personas, rules, skills, commands, tools, and similar assets from community lists, for example:

- [github/awesome-copilot](https://github.com/github/awesome-copilot)
- [affaan-m/ECC](https://github.com/affaan-m/ECC)
- [mattpocock/skills](https://github.com/mattpocock/skills)
- [addyosmani/agent-skills](http://github.com/addyosmani/agent-skills)
- [wshobson/agents](https://github.com/wshobson/agents)
- [hesreallyhim/awesome-claude-code](https://github.com/hesreallyhim/awesome-claude-code)
- [PatrickJS/awesome-cursorrules](https://github.com/PatrickJS/awesome-cursorrules)
- [ComposioHQ/awesome-codex-skills](https://github.com/ComposioHQ/awesome-codex-skills)

Imports must not override `.agents/AGENTS.md`; check license and policy fit.
