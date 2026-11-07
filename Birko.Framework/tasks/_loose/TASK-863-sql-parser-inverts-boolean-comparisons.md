---
id: TASK-863
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-859]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser inverts or mangles a boolean compared with a constant

## Context

Found by the TASK-859 review (2026-11-07), on SQLite against the C# oracle. These shapes already failed on HEAD,
before TASK-859:
- `x.Score.HasValue == false` renders `Score IS NOT NULL`: inverted.
- `(x.Score == x.Peer) == false` renders the equality itself: inverted.
- `x.Flag.HasValue && x.Flag.Value` renders `Flag = NULL`: matches nothing.

An inverted filter on a delete removes exactly the rows the caller meant to keep. Rules 41–44 apply.

## Acceptance criteria

- [ ] Reproduced per SQL provider against the C# oracle: each shape above, and the `== true`, `!= true`, `!= false`
      variants over `HasValue`, a comparison, a `bool` and a `bool?` column, and `.Value` on a `bool?`. Record each.
- [ ] Each one is translated exactly, or refused by name with `NotSupportedException` before the statement runs.
- [ ] Regression tests per shape and provider (add them to TASK-859's `NullFilterContract` or a sibling contract),
      red before the fix. Every SQL importer suite stays green.

## Out of scope

- Calls and casts that are dropped (TASK-860).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
