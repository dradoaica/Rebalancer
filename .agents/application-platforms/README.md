# Application platforms

**Platform-specific** configuration, deployment, and operational constraints (e.g., cloud PaaS, orchestrators, sidecar
runtimes). Each subdirectory names one platform or deployment target.

## When to read

Before changing **infrastructure-as-code**, deployment manifests, platform bindings, or environment-specific
configuration, read the relevant platform folder.

## Conventions

- Document: required services, networking, identity, secrets handling, scaling, and observability hooks.
- Keep platform docs separate from **runtime** toolchain docs (see `../application-runtimes/`).
- An empty subdirectory means **no written standard yet** and is **not applicable**: ignore it unless the task's
  change targets that platform. Then follow the approved specs and the existing configuration's conventions, never
  assume platform defaults without human confirmation, note the gap once in your summary, and draft standards only
  when the human asks.

## Authority

Platform changes that affect production or shared environments require explicit human approval per `.agents/AGENTS.md`
and `.agents/tools/allowed-tools.md`.
