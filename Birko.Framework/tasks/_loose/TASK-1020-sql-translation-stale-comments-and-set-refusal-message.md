---
id: TASK-1020
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1008, TASK-869, TASK-873, TASK-857]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Stale SQL translation comments and the SET refusal message

## Context

Found by [[TASK-1008]]'s pass 2 (method calls, casts and string matching) on 2026-11-28; the user asked for it to be filed.

- `Birko.Data.SQL/SQL/DataBase.cs` about lines 1012-1014 say a string method's comparison argument is intentionally
  ignored; since TASK-869 it is honoured or refused.
- About line 1311 says comparison semantics are delegated to the column collation; since TASK-873 a string list is exact
  (the comparer half is [[TASK-1017]]).
- The SET refusal message (about line 379) lists `GetValueOrDefault`, which is rewritten to `??` before it can reach
  that point, and leaves out the Math calls a SET refuses (TASK-857).

## Acceptance criteria

- [ ] The comments state what the code does now.
- [ ] The SET refusal message lists what a SET actually refuses; any test asserting the message is updated.

## Human test plan

N/A — fully covered by automated tests.
