# Decisions

For new Architectural Decision Records (ADRs), please use one of the following templates as a starting point:

* [adr-template.md](adr-template.md) has all sections, with explanations about them.
* [adr-template-minimal.md](adr-template-minimal.md) only contains mandatory sections, with explanations about them. <!-- ### Consequences also contained, though marked as "optional" -->
* [adr-template-bare.md](adr-template-bare.md) has all sections, which are empty (no explanations).
* [adr-template-bare-minimal.md](adr-template-bare-minimal.md) has the mandatory sections, without explanations. <!-- ### Consequences also contained, though marked as "optional" -->

The MADR documentation is available at <https://adr.github.io/madr/> while general information about ADRs is available at <https://adr.github.io/>.

## Naming

- `NNNN-kebab-case-title.md` (e.g., `0001-use-postgresql.md`); front matter `id`: `ADR-NNNN`.
- `NNNN` is the next free number on the **shared trunk**, never reused (see
  `.agents/protocols/multi-human-collaboration.md`).
- A replaced decision keeps its file and number.

## Metadata

Copy the front matter from the chosen template and keep every key.

- `decides` names the one concern decided; at most one `Accepted` ADR per concern.
- `depends-on` is the canonical dependency list (identifiers, not paths); `Sources` adds paths for readers and
  cites nothing missing from it.
- Only a human sets `approved-by`.
- Binding statuses take effect only once merged to the shared trunk.

## Statuses (meaning)

- **Proposed**: drafted (by an agent or human); not yet approved. Implementation must not start (Tier 1 gate).
- **Accepted**: approved by a human; implementation may proceed.
- **Rejected**: considered and declined; kept for the record.
- **Deprecated**: no longer applies, with no direct replacement.
- **Superseded**: replaced by a newer ADR, named in `superseded-by`; the newer ADR lists this one in `supersedes`.

## Review expectations (human vs. agent)

- Agents **draft** ADRs with status `Proposed`, and may list unverified options marked "unverified – please confirm".
- Only a **human** sets `Accepted`, `Rejected`, `Deprecated`, or `Superseded` (`.agents/rules/00-no-self-approval.md`).

## Index (this repository)

| ID   | File                                                                                                                                         | Status (YAML) |
|------|----------------------------------------------------------------------------------------------------------------------------------------------|---------------|
| 0000 | [0000-use-markdown-architectural-decision-records.md](0000-use-markdown-architectural-decision-records.md)                                   | accepted      |
| 0001 | [0001-pluggable-rebalancer-provider-and-client-lifecycle.md](0001-pluggable-rebalancer-provider-and-client-lifecycle.md)                     | accepted      |
| 0002 | [0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md](0002-coordinator-follower-lease-and-store-for-sqlserver-and-redis.md) | accepted      |
| 0003 | [0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md](0003-zookeeper-barriers-leader-election-and-rebalancing-modes.md)         | accepted      |
| 0004 | [0004-client-options-recovery-and-assignment-delay.md](0004-client-options-recovery-and-assignment-delay.md)                                 | accepted      |
| 0005 | [0005-dependency-injection-for-provider-registration-v2.md](0005-dependency-injection-for-provider-registration-v2.md)                       | accepted      |

See also [traceability matrix](../traceability-matrix.md).
