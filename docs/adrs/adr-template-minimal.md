---
# Required metadata (see docs/adrs/README.md). Keep keys; fill values.
id: ADR-NNNN               # NNNN = the number in this file's name
status: Proposed           # Proposed | Accepted | Rejected | Deprecated | Superseded
date: YYYY-MM-DD           # date of the last status change
decides: "{the single concern this record decides, e.g., primary datastore for the orders service}"
owner: "{author or accountable architect}"
approved-by: [ ]            # set by a human only
decision-makers: [ ]        # everyone involved in the decision
consulted: [ ]              # subject-matter experts consulted (two-way communication)
informed: [ ]               # people kept up to date (one-way communication)
depends-on: [ ]             # identifiers of records this decision relies on, e.g., [ADR-0003, US-0012]
supersedes: [ ]             # identifiers of records this decision replaces
superseded-by: ""          # set when status becomes Superseded, e.g., ADR-0123
---

# {short title, representative of a solved problem and found solution}

## Context and Problem Statement

{Describe the context and problem statement, e.g., in free form using two to three sentences or in the form of an illustrative story. You may want to articulate the problem in the form of a question and add links to collaboration boards or issue management systems.}

## Considered Options

* {title of option 1}
* {title of option 2}
* {title of option 3}
* … <!-- numbers of options can vary -->

## Decision Outcome

Chosen option: "{title of option 1}", because {justification. E.g., only option, which meets k.o. criterion decision driver | which resolves force {force} | … | comes out best (see below)}.

<!-- This is an optional element. Feel free to remove. -->

### Consequences

* Good, because {positive consequence, e.g., improvement of one or more desired qualities, …}
* Bad, because {negative consequence, e.g., compromising one or more desired qualities, …}
* … <!-- numbers of consequences can vary -->

## Sources

- {path and section for each identifier in `depends-on`, e.g., `docs/user-stories/US-0012-….md` (Acceptance criteria); cite nothing missing from `depends-on`}
