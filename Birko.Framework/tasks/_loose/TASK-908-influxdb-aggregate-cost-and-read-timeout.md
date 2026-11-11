---
id: TASK-908
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-907]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB: aggregate cost, native Flux and the read timeout

## Context

Measured by TASK-907 (2026-11-10, live container, 10 groups, Count + Sum): since TASK-906 every InfluxDB aggregate reads
every row and aggregates through `AggregateHelper`. 10k points ≈ 4.0 s; **100k points fail at the client's 10 s default
timeout** (any read of that size, not only aggregates). Reading 100k rows takes 45–50 s and ~290 MiB; the in-memory
aggregation 51–79 ms. A server-side Flux pivot + group + sum takes 46–49 s too: each point carries its own `Guid` tag, so
every point is its own series and the pivot dominates. Native Flux would not be faster without a schema change.

**Decide with the user:** drop native Flux (recommended: measured no faster), and whether the per-point `Guid` tag and the
10 s timeout should change.

**Decided by the user (2026-11-11):** the read timeout becomes configurable on the InfluxDB settings (the consumer's
value wins); reproduce the 100k-point failure first and document the series-per-point cost. Native Flux: re-measured after the point-layout change in TASK-910 (filed at the user's request); not built
here.

## Acceptance criteria

- [ ] The user's decision on native Flux recorded.
- [ ] The read timeout over ~100k points reproduced (rule 54) and fixed or documented; the series-per-point cost decided.

## Out of scope

- Aggregate correctness (TASK-906, TASK-907, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
