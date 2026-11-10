---
id: TASK-904
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-890]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# MySQL unsigned integer arithmetic may overflow silently

## Context

Recorded by TASK-890 (2026-11-10), not measured: TASK-890's MySQL guard wraps `int` / `short` / `sbyte` results only;
`uint` / `ulong` results are still computed in BIGINT and would answer instead of failing (an offset trick breaks on
UNSIGNED columns). Only relevant if unsigned columns are mapped (TASK-870's review found byte/uint/ulong are not
mappable columns today).

## Acceptance criteria

- [ ] Establish whether an unsigned member can reach SQL arithmetic at all; if not, record and close.
- [ ] If it can: measure against checked C# and guard or refuse by name; tests red before the fix.

## Out of scope

- Signed arithmetic (TASK-890, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
