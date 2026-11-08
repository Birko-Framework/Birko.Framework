---
id: TASK-871
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL reader paths pass `OnExecute` the raw command text, without values

## Context

Found by TASK-858 (2026-11-08), by reading the code; not reproduced. The reader paths
(`AbstractConnector.cs` around line 1229, `AbstractAsyncConnector.cs` around 364) pass `OnExecute` the raw command text
with parameter placeholders. Other paths pass the diagnostic text with values substituted (`GetGeneratedQuery`,
TASK-832 and TASK-853). A subscriber logging queries therefore sees two different formats, and on reads it sees no
values.

## Acceptance criteria

- [ ] Reproduce first (rule 54): subscribe to `OnExecute` and run each statement kind (read, count, insert, update,
      delete, view read), sync and async. Record what text each one receives.
- [ ] Every path passes the same form, from one producer. Decide whether that is the substituted diagnostic text or
      the raw text plus parameters, and keep TASK-832's linear cost.
- [ ] Tests per statement kind, red before the fix.

## Out of scope

- How values are rendered (TASK-853, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
