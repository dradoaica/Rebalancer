# Service Design Packages (SDPs)

SDPs live in this folder: `docs/sdps/`.

Use the template:

- `sdp-template.md`

## Naming

- `SDP-<service-name>.md` (e.g., `SDP-orders-api.md`); front matter `id`: the file name without `.md`.
- One SDP per service; revisions bump `version` in the same file. Check the shared trunk for an existing SDP first.

## Metadata

Copy the front matter from `sdp-template.md` and keep every key.

- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Draft**: incomplete; collecting requirements and operational expectations.
- **Proposed**: ready for human review; open questions are explicit.
- **Accepted**: approved baseline for service design and operational commitments.
- **Retired**: no longer applicable (service replaced/decommissioned).

## Review expectations (human vs. agent)

- Agents (typically solutions/systems architects) **draft** SDPs.
- Humans **review/approve** before treating SLAs/SLOs, security/privacy commitments, and operational support models as binding.

## Must-haves per SDP

- Service purpose, scope, consumers
- Dependencies (upstream/downstream/external)
- NFRs (availability/latency/retention/RPO-RTO)
- Security & privacy requirements
- Operations: monitoring, alerting, runbooks, on-call expectations
- Transition plan: deploy/rollback/decommission
- `Sources` links to relevant decision records and user stories

