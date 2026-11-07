---
id: TASK-852
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-836]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A re-bound SQL parameter keeps the type it was created with

## Context

Flagged by the bulk-filter-operations respec (2026-11-07). The behaviour predates TASK-836. The user chose to file it.
- `AbstractConnectorBase.AddParameter` overwrites an existing name's value, which the multi-row insert relies on (it
  re-binds the same names per row).
- The provider's `CreateParameter` runs only when the parameter is first created. That is where SQL Server sets
  `DateTime2` (TASK-656).
- A name first bound to one CLR type and re-bound to another keeps the first type's `DbType` / `SqlDbType`. For
  example: null, then a `DateTime`; or an `int`, then a `decimal`.

## Acceptance criteria

- [ ] Reproduced on SQL Server live, and on the other providers where type inference applies: a re-bound parameter
      whose value type changes is sent with the old type. Record what the server receives (truncation, conversion
      error, or a silently wrong value). If nothing observable goes wrong, close with the measurement.
- [ ] Re-binding applies the same typing as creation (one producer, rule 28), or the value-type change is refused.
- [ ] Regression test red before the fix.

## Out of scope

- `AddParameter`'s lookup cost (TASK-836, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
