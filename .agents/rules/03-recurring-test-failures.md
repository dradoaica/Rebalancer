# Rule 03 – Recurring test failures get a problem record

## Scope

Applies to every AI agent in this repository, and to every human reviewer, whenever a test fails during a change,
a release precondition, a CI run or a replay: unit, integration, model checking or a rehearsal. Introduced by
PRB-0009's closure.

## Rule statements

- An agent **MUST** record every test failure it sees where the change or release records its results (the change
  record's Preconditions, the MR/PR description): the test, the run, the error, and a seed or log when there is one.
- When the same test fails a **second** time, in any run, on any branch, an agent **MUST** open a problem record in
  `docs/problem-records/` (status `Open`), unless one already covers that failure, and cite it wherever the failure is
  recorded.
- An agent **MUST NOT** give "load", "flaky", "timing" or "environment" as a failure's cause without evidence that
  shows it: a replay that passes under the conditions named and fails without them, a trace, or a log. Without such
  evidence the cause is recorded as unknown.
- A failure that is reported as **not caused by the change** **MUST** come with evidence: the same failure on the base
  commit, or a replay.
- An agent **SHOULD** replay a failing randomised or timing-sensitive test with its seed, alone, before attributing
  the failure to anything.

## Rationale

PRB-0009: a Redis liveness failure was put down to load in US-0025 and CR-0013, and again in CR-0014 before a
replay alone disproved it. The cause, a design defect in the Redis backend's writes, was found only when a problem
record was opened and a trace was taken; it needed two decision records and two releases to fix.

## Examples

- **Good:** "`RedisNetworkFaultTests` (Valkey) failed for the second time (seed `1196549739`, CR-0014 Preconditions):
  PRB-0009 opened. It also fails alone on the base commit, so not caused by this change."
- **Bad:** "One failure, `RedisNetworkFaultTests` on Redis 7.0, liveness; load-sensitive, passes when run alone",
  recorded in US-0025 and again in CR-0013, with no problem record and no replay that shows load is the cause.

## Enforcement

Human review at the MR/PR and at change-record approval (`.agents/protocols/pre-mr-gate.md`, check 1): the reviewer
checks that every failure is recorded, that a repeated one cites a problem record, and that any cause given comes with
its evidence. Not mechanically checked (`scripts/govern_check.py` does not read test results).
