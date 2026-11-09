---
id: TASK-880
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL Server: an error raised while rows are produced turns a read into an empty result

## Context

Found by TASK-874 (2026-11-09), measured live on SQL Server 2022: `ReadAsync(x => x.Hits / (x.Hits - x.Hits) == 0)`
returns 0 rows, while `CountAsync` with the same filter throws "Divide by zero". A LIKE pattern past 4000 characters did
the same before TASK-874 refused it. `AbstractAsyncConnector`'s reader loop stops as soon as `HasRows` is false, so an
error that arrives after the result's metadata is never read. Rule 26: a reader that answers an error with an empty
result gives a wrong answer. Any runtime error in a read (overflow, conversion, divide by zero) is silently an empty
list.

## Acceptance criteria

- [ ] Reproduce (rule 54) per provider, sync and async, reads, paged reads, view reads, the SELECT before
      `Update(filter, Action)`: divide by zero, conversion and overflow errors. Record which return empty.
- [ ] Every reader path drains the result so a server error reaches the caller (one producer for the read loop).
- [ ] Tests, red before the fix.

## Out of scope

- Which filters are translated (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
