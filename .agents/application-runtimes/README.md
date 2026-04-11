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

If a runtime folder has no content, propose minimal standards with the human rather than inventing org-wide defaults.
