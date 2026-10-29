# Vendor adapters

> Part of the canonical contract; same precedence as `.agents/AGENTS.md`.

Vendor adapter files must defer to `.agents/AGENTS.md`. They may add platform-specific mechanics (e.g., tool bindings,
model selection), never contradictory governance.

| Vendor file                            | Tool                                                    | How `.agents/AGENTS.md` gets loaded               |
|----------------------------------------|---------------------------------------------------------|---------------------------------------------------|
| `AGENTS.md` (repository root)          | Codex, OpenCode, and similar                            | Agent reads it (explicit first step)              |
| `GEMINI.md` (repository root)          | Gemini CLI; `@` imports `.gemini/GEMINI.md`             | Automatic, via `.gemini/GEMINI.md`                |
| `.gemini/GEMINI.md`                    | Gemini CLI modular context                              | Automatic (`@../.agents/AGENTS.md` import)        |
| `.github/copilot-instructions.md`      | GitHub Copilot (VS Code and others)                     | Agent reads it (explicit first step)              |
| `.claude/CLAUDE.md`                    | Claude Code                                             | Automatic (`@../.agents/AGENTS.md` import)        |
| `.cursor/rules/co-governed-a-sdlc.mdc` | Cursor (`alwaysApply` rule)                             | Agent reads it (explicit first step)              |
| `.windsurfrules`                       | Windsurf Cascade                                        | Agent reads it (explicit first step)              |
| `.clinerules`                          | Cline & Roo Code                                        | Agent reads it (explicit first step)              |
| `.junie/guidelines.md`                 | JetBrains Junie / AI Assistant                          | Agent reads it (explicit first step)              |
| `.amazonq/rules/project-rules.md`      | Amazon Q Developer                                      | Agent reads it (explicit first step)              |
| `.kiro/steering/co-governed-a-sdlc.md` | Kiro (IDE and CLI; successor to Amazon Q Developer CLI) | Automatic (`#[[file:.agents/AGENTS.md]]` include) |
| `CONVENTIONS.md`                       | Aider and similar CLI agents                            | Agent reads it (explicit first step)              |
| `.cody/instructions.md`                | Sourcegraph Cody                                        | Agent reads it (explicit first step)              |
| `.zed/rules.md`                        | Zed AI assistant                                        | Agent reads it (explicit first step)              |
| `.openai/instructions.md`              | OpenAI tooling                                          | Agent reads it (explicit first step)              |
| `.ai/instructions.md`                  | Generic `.ai/` entry point                              | Agent reads it (explicit first step)              |
| `.agent/instructions.md`               | Generic `.agent/` entry point                           | Agent reads it (explicit first step)              |

## Adapter requirements

- **Import** `.agents/AGENTS.md` where the tool supports it (e.g., `@path` in Claude Code and Gemini CLI, relative to
  the importing file; `#[[file:path]]` in Kiro steering, relative to the workspace root).
- **Otherwise**, make reading `.agents/AGENTS.md` the explicit first step of every task.
- **Never copy** its content into an adapter; copies drift.
- Add every new adapter to the table above.
- **Skills:** `.agents/skills/` stays the only copy. If the tool does not scan `.agents/skills/`, expose it with a
  relative symlink from the tool's project skills directory (e.g., `.claude/skills` → `../.agents/skills`), never a
  copy. Record the tool in `.agents/skills/README.md` → Skill discovery.

**Rule:** If a vendor file contradicts `.agents/AGENTS.md`, `.agents/AGENTS.md` wins. Surface the contradiction to the
human.
