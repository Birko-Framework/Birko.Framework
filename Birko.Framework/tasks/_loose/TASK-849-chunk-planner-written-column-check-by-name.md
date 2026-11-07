---
id: TASK-849
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The IN-list chunk planner decides "this update writes the filtered column" by comparing bare names

## Context

Flagged by the bulk-filter-operations respec (2026-11-07), from reading the code; not reproduced. The user chose to
file it.
- TASK-833's review found that a chunked UPDATE writing the filtered column applies twice: a row moved into a later
  chunk's values matches again. The fix never splits such an update.
- The planner identifies the written columns by text: `WrittenColumns` / `BareColumn` split each SET entry on `=` and
  compare bare names.
- A SET entry whose target that split does not isolate escapes the check, and the update is chunked. Examples: a
  qualified or quoted name in a form the comparison does not normalise, or a computed SET from TASK-826.
- The result would be the double increment again, silently.

## Acceptance criteria

- [ ] Probe every SET form the update paths can render (plain, `PropertyUpdate` Set / Increment / Compute, expression
      values, qualified and quoted names, each provider's quoting) for one whose written column the check misses.
      Record the result.
- [ ] The written columns come from the update's own field list (the `PropertyUpdate` assignments or the fields
      dictionary), not from parsing rendered SQL text. One producer, shared with the statement builder (rule 16).
- [ ] Regression test: each form that writes the filtered column is never chunked, and is refused past the limit.
      Red before the fix if a form escaped.

## Out of scope

- The chunking rules themselves (TASK-833).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
