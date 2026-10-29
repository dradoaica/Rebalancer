# Application runtimes

**Runtime-specific** toolchain standards: language versions, SDKs, package managers, build/test commands, and baseline
coding standards. Each subdirectory names one runtime or language ecosystem.

## When to read

Before adding dependencies, changing build scripts, or tuning compiler/interpreter settings, read the matching runtime
folder.

## Conventions

- Pin or document supported versions (e.g., minimum Node, .NET, JDK).
- List canonical commands for format, lint, test, and build where the team has agreed on them.
- Distinguish **runtime** (language VM, compiler) from **framework** (app structure in `../application-frameworks/`) and
  **platform** (hosting in `../application-platforms/`).

## Gaps

An empty subdirectory means **no written standard yet** and is **not applicable**: ignore it unless the task's code uses
that runtime. Then follow the approved specs and the existing code's conventions, never invent org-wide defaults, note
the gap once in your summary, and draft standards only when the human asks.
