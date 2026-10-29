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

{Describe the context and problem statement, e.g., in free form using two to three sentences or in the form of an illustrative story. You may want to articulate the problem in the form of a question. Consider adding links to collaboration boards or issue management systems. Make the scope of the decision explicit, for instance, by calling out or pointing at structural architecture elements (components, connectors, ...)}

<!-- This is an optional element. Feel free to remove. -->

## Decision Drivers

* {decision driver 1, for instance, a desired software quality, faced concern, constraint or force}
* {decision driver 2}
* … <!-- numbers of drivers can vary -->

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

<!-- This is an optional element. Feel free to remove. -->

### Confirmation

{Describe how the implementation / compliance of the ADR can/will be confirmed. Is there any automated or manual fitness function? If so, list it and explain how it is applied. Is the chosen design and its implementation in line with the decision? E.g., a design/code review or a test with a library such as ArchUnit can help validate this. Note that although we classify this element as optional, it is included in many ADRs.}

<!-- This is an optional element. Feel free to remove. -->

## Pros and Cons of the Options

### {title of option 1}

<!-- This is an optional element. Feel free to remove. -->
{example | description | pointer to more information | …}

* Good, because {argument a}
* Good, because {argument b}

<!-- use "neutral" if the given argument weights neither for good nor bad -->

* Neutral, because {argument c}
* Bad, because {argument d}
* … <!-- numbers of pros and cons can vary -->

### {title of another option}

{example | description | pointer to more information | …}

* Good, because {argument a}
* Neutral, because {argument b}
* Bad, because {argument c}
* …

<!-- This is an optional element. Feel free to remove. -->

## More Information

{You might want to provide additional evidence/confidence for the decision outcome here and/or document the team agreement on the decision and/or define when/how this decision the decision should be realized and if/when it should be re-visited. Links to other decisions and resources might appear here as well.}

## Sources

- {path and section for each identifier in `depends-on`, e.g., `docs/user-stories/US-0012-….md` (Acceptance criteria); cite nothing missing from `depends-on`}
