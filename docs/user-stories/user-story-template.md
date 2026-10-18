---
# Required metadata (see docs/user-stories/README.md). Keep keys; fill values.
id: US-NNNN                # NNNN = the number in this file's name
status: Draft              # Draft | Ready | In progress | Done | Canceled
date: YYYY-MM-DD           # date of the last status change
owner: ""                  # who implements it; set when status becomes In progress
approved-by: []            # readiness approval; set by a human only
priority: ""               # e.g., Must | Should | Could
ssdlc-phase: Planning      # Planning | Design | Coding | Testing | Release | Maintenance
depends-on: []             # identifiers, e.g., [ADR-0003, SDP-orders]
---

# User story: {short title}

## Story

**As a** {role}  
**I want** {capability}  
**So that** {outcome / value}

## Context

{Why now, constraints, links to incidents or decisions.}

## Acceptance criteria

1. {Testable criterion}
2. {Testable criterion}

## Non-goals

- {What this story explicitly does not cover}

## Dependencies

- Decision records: `docs/adrs/…` (if any)
- Other stories / services: {links}

## Security & compliance

{Threats, data classification, PII, AuthZ—or "None / N/A" with rationale.}

## Definition of ready

- [ ] Governing ADR referenced (or marked "N/A - non-architectural")
- [ ] Acceptance criteria are testable, unambiguous, and numbered
- [ ] Security & compliance considerations addressed
- [ ] Dependencies and non-goals are explicitly identified

## Definition of done

- [ ] Acceptance criteria met
- [ ] Tests / checks agreed with a team
- [ ] Docs / runbooks updated if needed
- [ ] Traceable to change record (if released) - `docs/change-records/…`

## Sources

- {ADR, SDP, change record, or spec paths}
