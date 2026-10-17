---
name: ponytail-gain
description: >
  Show ponytail's measured savings (code, cost, speed) from the benchmark.
  One-shot display. Use for /ponytail-gain, "what does ponytail save",
  "ponytail impact".
---

# Skill: Ponytail Gain (Efficiency Scoreboard)

## Purpose

Displays measured empirical efficiency gains (code reduction, cost savings, latency improvements, test pass rates) based
on published agentic benchmarks.

## Inputs required

- User trigger (`/ponytail-gain` or efficiency query).

## Preconditions / governance gates

- **Read-Only / Display Only:** Changes no configuration, state, or repository files.
- **Anti-Delusional Grounding:** Discloses benchmark provenance clearly; forbids fabricating unmeasured per-repository
  savings.

## Procedure (mandatory)

1. **Verify invocation parameters**:
    - Confirm read-only display intent without modifying execution mode.
2. **Render standardized benchmark scoreboard**:
    - Display benchmark metrics from published dataset (Opus 5.5, 39 tasks × 5 runs).
    - Present ASCII comparative bars representing code volume, tokens, cost, and execution duration:
      ```
        ponytail gain           benchmark · 39 tasks × 5 runs · Opus 5.5
 
        no-skill        ████████████████████  100%
        Lines of code   █████████···········   47%   ▼ 53%
        Output tokens   ███████████·········   55%   ▼ 45%
        Cost            ███████████████·····   74%   ▼ 26%
        Time            ████████████········   59%   ▼ 41%
        Hidden checks passed   97%  (no-skill 96%)
        Tests where the logic needs one   98%  (no-skill 68%)
 
        This repo:  /ponytail-debt  (shortcuts you deferred)
                    /ponytail-audit (what's still cuttable)
      ```
3. **State honesty boundaries**:
    - Explicitly note that figures reflect benchmark averages rather than live telemetry from the current repo.
    - Point human to `/ponytail-debt` and `/ponytail-audit` for repository-specific metrics.

## Outputs

- Formatted ASCII efficiency scoreboard.

## Validation

- Confirm output contains benchmark attribution and disclaimer against speculative repo savings.

## Escalation points

- None (informational display).

## Sources

- Upstream: `DietrichGebert/ponytail`
- Anti-Delusional Protocol: `.agents/AGENTS.md`
