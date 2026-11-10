---
id: TASK-897
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-885]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB aggregates: result types, DateTime MIN/MAX as text, case-sensitive names

## Context

Found by TASK-885 (2026-11-10):
- Results come back as long/double, not the member's C# type (TASK-878 converts to the C# type on Elasticsearch).
- MIN/MAX of a `DateTime` returns its stored text, which also sorts wrongly when fractional seconds are mixed
  (`"…00.5Z"` < `"…00Z"`).
- GROUP BY and aggregate names are sent verbatim and case-sensitively; LINQ matches any case.
- SUM/AVG of a non-numeric member is not refused (LINQ throws `NotSupportedException`).

## Acceptance criteria

- [ ] Reproduce each (rule 54) live against LINQ.
- [ ] Each matches LINQ or is refused by name.
- [ ] Tests, red before the fix.

## Out of scope

- Nullable aggregates and the time bucket (TASK-885, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
