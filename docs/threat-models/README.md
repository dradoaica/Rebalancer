# Threat models

Threat models live in this folder: `docs/threat-models/`.

Use the template:

- `threat-model-template.md`

## Purpose

Threat modeling is a standard governance artifact used to systematically identify, assess, and mitigate security risks for a system, service, or change. It complements ADRs (decision records) and SDPs (service design packages) by capturing assumptions, assets, data flows, threats, and mitigations early and maintaining them over the service lifecycle.

## Naming

- `TM-NNNN-kebab-case-title.md` (e.g., `TM-0001-orders-api.md`); front matter `id`: `TM-NNNN`.
- `NNNN` is the next free number on the **shared trunk**, never reused (see
  `.agents/protocols/multi-human-collaboration.md`).

## Metadata

Copy the front matter from `threat-model-template.md` and keep every key.

- `scope` names what the model covers; at most one `Accepted` model per scope.
- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Draft**: being written/refined; inputs and diagrams may be incomplete.
- **Proposed**: ready for security/architecture review; mitigations identified.
- **Accepted**: reviewed/approved; is the current baseline for the scoped system.
- **Superseded**: replaced by a newer threat model; keep for traceability and diffing.

## Review expectations (human vs. agent)

- Agents (typically personas `systems-architect`, `devsecops-engineer`) draft threat models using the template.
- Humans with security/architecture responsibility review and approve before high‑risk changes proceed.

## Scope & relationship to other artifacts

- A threat model should reference and be referenced by relevant SDPs under `docs/sdps/`.
- ADRs that materially affect the attack surface should either:
  - be cited in this model’s `Sources`, and/or
  - trigger an update to this model (status change or a new version) if risks/mitigations change.
- Change Records should reference the accepted threat model when the change affects risk posture.

## Contents checklist (must-haves)

- Clear scope and boundaries (system context, trust boundaries)
- Asset and data classification
- Data flow diagrams or structured flows
- Threat analysis (e.g., STRIDE), with risks rated and mapped to mitigations/controls
- Residual risk and acceptance rationale
- Approvals (roles as per your organization)
- `Sources` linking to ADRs, SDPs, change records, and any external standards
