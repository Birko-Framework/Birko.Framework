---
id: TASK-1015
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
related: [TASK-1008, TASK-867]
findings: []
pr: null
github-issue: null
jira-key: null
---

# NullOrderingFold's remark says more than the fold does

## Context

Found by [[TASK-1008]]'s pass 1 (NULL semantics) on 2026-11-28; the user asked for it to be filed.

The remark on `Birko.Data.Core/Expressions/NullOrderingFold.cs` (line 28) says a boolean constant survives the fold only
as the whole body. Its own tests show `False` surviving inside `x.Flag == False` and inside a ternary branch
(`IIF(…, False, x.Flag)`). RavenDB normalizes after the fold, which desugars the ternary; Elasticsearch folds after
normalizing, so `Flag == False` reaches its translator as is. Probably harmless, but a reader trusting the remark would
assume no translator ever sees a constant inside a filter.

## Acceptance criteria

- [ ] Check whether any translator downstream of the fold (RavenDB, Cosmos DB, MongoDB, Elasticsearch) mistranslates
  a surviving inner constant; a wrong answer becomes its own bug task.
- [ ] The remark states what the fold actually reduces.

## Human test plan

N/A — fully covered by automated tests.
