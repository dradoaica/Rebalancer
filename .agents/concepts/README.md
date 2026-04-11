# Concepts (framework reference)

Canonical reference material for the governing frameworks used in this repository. **Read the relevant concept file
before producing any artifact that depends on it.**

## Layout

| Path                                      | Topic                                                           |
|-------------------------------------------|-----------------------------------------------------------------|
| `ssdlc.md`                                | Secure SDLC phases, OWASP SAMM, NIST SSDF                       |
| `agile.md`                                | Agile ceremonies, backlog, user stories in `docs/user-stories/` |
| `togaf/architectural-decision-records.md` | Decision records, mutability, decision log                      |
| `itil/change-records.md`                  | Change management; points to `docs/change-records/`             |
| `itil/problem-records.md`                 | Problem management; points to `docs/problem-records/`           |
| `itil/service-design-packages.md`         | SDPs; points to `docs/sdps/`                                    |

## Rules

- If a concept file is **empty or missing**, do not improvise that framework’s governance; draft or bootstrap the
  minimal artifact with human approval.
- ITIL paths under `docs/` must exist before treating those processes as live; see `.agents/AGENTS.md` for bootstrap
  guidance.

## Upstream

`.agents/AGENTS.md` describes how concepts relate to skills, rules, commands, and tools.
