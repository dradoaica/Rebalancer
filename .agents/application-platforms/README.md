# Application platforms

**Platform-specific** configuration, deployment, and operational constraints (e.g., cloud PaaS, orchestrators, sidecar
runtimes). Each subdirectory names one platform or deployment target.

## When to read

Before changing **infrastructure-as-code**, deployment manifests, platform bindings, or environment-specific
configuration, read the relevant platform folder.

## Conventions

- Document: required services, networking, identity, secrets handling, scaling, and observability hooks.
- Keep platform docs separate from **runtime** toolchain docs (see `../application-runtimes/`).
- Empty subdirectories mean **no written standard yet**; do not assume defaults without human confirmation.

## Authority

Platform changes that affect production or shared environments require explicit human approval per `.agents/AGENTS.md`
and `.agents/tools/allowed-tools.md`.
