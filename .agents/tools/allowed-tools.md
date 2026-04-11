# Allowed tools (authoritative allow-list)

## Scope

Applies to all AI agents operating in this repository.

## Policy

- A tool is **allowed** only if it is listed below (or is an obvious sub-capability of a listed tool, e.g., reading a
  file after listing a directory).
- If the human explicitly authorizes a tool in the current session, the agent may use it **only for that session** and
  must note that authorization in the response `Sources` block.

## Allow-list

### Repository file access

- **Read and search files** in the repository (including ripgrep-style search).

Constraints:

- Do not exfiltrate or paste secrets from outside the repository unless the human explicitly provided them in-session.
- Prefer the smallest scope needed.

### Local shell

- **Read-only diagnostics** (allowed): tests, lint, format check, build analysis, `git status`, `git diff`, dependency
  tree inspection.
- **Side-effect commands** (requires explicit human approval): deploy, publish, push to shared remotes, infrastructure
  changes, secret rotation, anything affecting non-local systems.

Constraints:

- No destructive commands (`rm -rf`, disk wipes, etc.) unless explicitly requested and confirmed.
- No force-push unless explicitly requested.

### Network / web

- **Fetch public documentation** when needed to verify APIs or standards; cite sources.

Constraints:

- Do not access credential-protected systems unless the human provided access in-session.

## Human-approval gates (non-exhaustive)

Explicit approval is required before:

- merging or pushing to shared branches
- deployments to non-local environments
- infrastructure or secrets changes
- any action that is not fully reversible
