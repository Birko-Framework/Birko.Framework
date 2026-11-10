---
id: TASK-906
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-886]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: leftovers after TASK-886

## Context

Found by TASK-886 (2026-11-10):
- **RavenDB view index: MIN of {3, null} gives null and AVG gives 1.5**, where C# gives 3 and 3 (measured; nulls are
  not skipped). Silently wrong.
- **MongoDB store `Aggregate` ignores `OrderBy`, `Offset`, `Limit` and time bucketing**, and leaves non-numeric MIN/MAX
  types as the driver maps them.
- **InfluxDB native Flux path** (async, single aggregate, no filter) not measured: probably no row for an ungrouped query
  over nothing, and drops a group whose field is all null.
- **Mongo view decimal sums:** the empty-row Sum is an `Int32` 0 deserialised into the view property; not measured for a
  `decimal` view property.
- `docs/specs/views-and-aggregation.md` still describes "no aggregates and no grouping returns raw rows"; needs a
  `/specs regen`.

## Acceptance criteria

- [ ] Reproduce each (rule 54) against `AggregateHelper.LinqAggregate`.
- [ ] Each matches the oracle or is refused by name.
- [ ] Tests, red before each fix.

## Out of scope

- The decided empty and NULL answers (TASK-886, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
