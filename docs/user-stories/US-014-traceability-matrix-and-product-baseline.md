# User story: Traceability matrix and product baseline documentation

## Metadata

| Field           | Value                                              |
|-----------------|----------------------------------------------------|
| **ID**          | US-014                                             |
| **Status**      | Done                                               |
| **Priority**    | Should                                             |
| **Owner**       |                                                    |
| **SSDLC phase** | Planning                                           |

## Story

**As a** contributor or agent working under the Co-Governed Agentic SDLC  
**I want** a maintained traceability matrix and a concise product baseline spec  
**So that** decisions, stories, code, and tests stay aligned and onboarding does not depend on tribal knowledge

## Context

[docs/traceability-matrix.md](../traceability-matrix.md) and [docs/initial-product-specs/rebalancer-product-baseline.md](../initial-product-specs/rebalancer-product-baseline.md) are introduced as living artifacts. This story defines what “done” means for the **documentation v2 slice**.

## Acceptance criteria

1. `docs/traceability-matrix.md` exists and maps **accepted** ADRs **0000–0004** to user stories **US-001–US-011** and primary repository paths (`src/`, `examples/`, `tests/`); **proposed** ADR 0005 and **US-012–US-014** appear with clear **Planned** / **Draft** status in the matrix notes.
2. `docs/initial-product-specs/rebalancer-product-baseline.md` states product intent, in-scope backends, explicit non-goals for the current .NET library, and links to ADRs and the traceability matrix.
3. `docs/initial-product-specs/README.md` links to the baseline document.
4. Root `README.md` includes a **Documentation** subsection linking to the matrix, baseline, architecture overview, and `docs/adrs/`.

## Non-goals

- Full Service Design Package (SDP) or ITIL change-record automation in this story.
- Translating the baseline into multiple languages.

## Dependencies

- Decision records: references `docs/adrs/0000`–`0005` as applicable
- Other stories: N/A

## Security & compliance

Baseline and matrix must not embed live secrets; reference configuration patterns only.

## Definition of done

- [x] Acceptance criteria met
- [x] Tests / checks agreed with team (N/A — documentation story)
- [x] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) — `docs/change-records/…`

## Sources

- `.agents/AGENTS.md`
- `docs/traceability-matrix.md`
- `docs/initial-product-specs/rebalancer-product-baseline.md`
- `README.md`
