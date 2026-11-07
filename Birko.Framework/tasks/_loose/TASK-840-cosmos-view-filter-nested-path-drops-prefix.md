---
id: TASK-840
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB view filter on a nested property reads the wrong field

## Context

Found while probing TASK-839 (2026-11-07), from reading the code; not yet reproduced. The user chose to file it as
its own task.

- `CosmosFilterTranslator.TranslateFieldAccess` (Birko.Data.CosmosDB.Views) renders `v.Inner.X` as `c.X`, keeping
  only the last member name.
- A filter on a nested property therefore compares the wrong field, which is usually missing on the document, so it
  matches nothing, or it matches a same-named top-level field. Either way the answer is wrong and nothing is raised
  (rules 41–44).

## Acceptance criteria

- [ ] Reproduced first on the live emulator: a view with a nested property, filtered on it, returns the wrong rows
      today. Record what it returns.
- [ ] A nested path renders in full (`c.Inner.X`, through `MapViewPropertyToSource` where the view maps it), or a
      nested path that cannot be mapped refuses by name before any request. The read equals the C# oracle.
- [ ] The regression test is red before the fix.

## Out of scope

- `Contains` support (TASK-839).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
