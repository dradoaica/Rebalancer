# Threat model: {short title}

## Metadata

| Field            | Value                                       |
|------------------|---------------------------------------------|
| **ID**           | TM-{number-or-key}                          |
| **Status**       | Draft \| Proposed \| Accepted \| Superseded |
| **Scope**        | {system/service/change this model covers}   |
| **Owners**       | {service owner / security / architect}      |
| **Last updated** | YYYY-MM-DD                                  |

## Summary

{One paragraph describing the system boundaries, key assets, and the risk posture in brief.}

## System context and boundaries

- **In scope:** {components, interfaces, environments}
- **Out of scope:** {explicitly excluded elements}
- **Assumptions:** {operational, architectural, trust assumptions}
- **Trust boundaries:** {identify boundaries such as device, network segments, tenant, process}

## Assets and data classification

List the primary assets and their data classification. Example:

| Asset / Data                        | Classification (e.g., Public / Internal / Confidential / Restricted) | Notes |
|-------------------------------------|----------------------------------------------------------------------|-------|
| {Customer PII at rest in DB}        | Restricted                                                           | {…}   |
| {Service machine identity / tokens} | Confidential                                                         | {…}   |
| {Operational metrics / logs}        | Internal                                                             | {…}   |

## Data flows

Describe key data flows using a diagram (link) or structured steps:

1. {Actor/System A} → {Interface} → {System B} — {what data and purpose}
2. {…}

If using a diagram, store it under `docs/diagrams/` and link it here.

## Threat analysis (STRIDE)

Use STRIDE (or another method) across assets, data flows, and trust boundaries. Example structure:

### Spoofing

- Threat: {…}
- Affected flow/asset: {…}
- Mitigation(s): {authn method, mTLS, signed tokens, etc.}
- Risk: {Low/Medium/High} — {rationale}

### Tampering

- Threat: {…}
- …

### Repudiation

- Threat: {…}
- …

### Information Disclosure

- Threat: {…}
- …

### Denial of Service

- Threat: {…}
- …

### Elevation of Privilege

- Threat: {…}
- …

## Controls mapping

Map mitigations to concrete controls and references:

| Threat / Risk                    | Control (design or runtime)                  | Evidence / Reference                             |
|----------------------------------|----------------------------------------------|--------------------------------------------------|
| {Token theft via header leakage} | `SameSite=strict`, CSRF token, mTLS          | ADR `docs/adrs/NNNN-…`, SDP section, config path |
| {PII at rest exposure}           | AES-256, KMS-managed keys, role-based access | SDP Security & privacy; runbook; KMS policy link |

## Residual risk and acceptance

- Residual risks after mitigations: {list}
- Acceptance rationale: {why acceptable; compensating controls}
- Decision: {Accepted by <role> on <date>}

## Dependencies and follow-ups

- Required ADRs: `docs/adrs/…`
- Required SDPs: `docs/sdps/…`
- Change records to implement mitigations: `docs/change-records/…`
- Runbooks / monitors to add: {links}

## Approvals

| Role                    | Name | Date |
|-------------------------|------|------|
| Service owner           |      |      |
| Security / architecture |      |      |

## Sources

- ADRs: `docs/adrs/…`
- SDP: `docs/sdps/…`
- Related user stories: `docs/user-stories/…`
- External standards: {e.g., NIST 800-53, ISO 27001 control IDs}
