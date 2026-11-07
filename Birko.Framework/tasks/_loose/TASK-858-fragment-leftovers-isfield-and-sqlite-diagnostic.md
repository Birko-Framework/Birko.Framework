---
id: TASK-858
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-856]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Two leftovers from the value-fragment work: a dropped `IsField`, and SQLite's diagnostic text

## Context

Found by TASK-856 (2026-11-07). Grouped, because both come from the same thread and are small.
- **`DataBase.ReturnSingleSubCondition`** does not copy `IsField` when it collapses a group. This predates TASK-856 and
  no current path exercises it. A collapsed column-vs-column comparison would render its right side as a value instead
  of a column.
- **On SQLite**, `GetGeneratedQuery`'s diagnostic text shows the portable fragment, not the SQL TASK-856's dialect
  actually sent. A reader debugging a SQLite failure sees a statement that never ran.
- **Added from the TASK-856 review:**
  - `DataBase.ComparedFragment` (around line 1793): on SQLite, constants inside a fragment are evaluated again at
    render time, and once per IN chunk. A side-effecting call (`x.Amount + Next()`) runs several times, and a captured
    variable that changes between parsing and rendering makes the executed SQL differ from the `Name` the planner
    checked. Low real-world impact.
  - `Birko.Data.SQL.View/SQL/DataBase_ViewConditions.cs` (around line 71): `ResolveViewCondition` builds a new
    `Condition` and drops `NameFragment` / `FieldFragment`, so persistent SQLite views keep the portable form. Most
    likely loud ("no such column"), since the fragment's columns are not remapped there.
  - "Other providers render byte-identically" is pinned by one shape only
    (`ThePortableFragment_OtherProvidersRun_IsUnchanged`). It should also cover CASE, COALESCE, a Convert, and column
    vs column.

## Acceptance criteria

- [ ] `ReturnSingleSubCondition` carries every field of the condition it collapses, preferably through
      `Condition.Copy` (one producer). A test collapses a column-vs-column condition and asserts it still renders as
      columns. If no path can collapse such a condition, say so and pin it.
- [ ] The diagnostic text equals the command text actually executed on SQLite, with the values substituted. Test:
      the diagnostic of a decimal-fragment filter on SQLite contains the dialect's rendering.
- [ ] Fragment constants are evaluated once, at parse time, and the rendering reuses that value: a test with a
      side-effecting captured call shows one evaluation across chunks.
- [ ] Persistent SQLite views carry the fragments through `ResolveViewCondition`, or refuse a decimal fragment by
      name. Test on a persistent view.
- [ ] The byte-identity test for the other providers covers CASE, COALESCE, a Convert, and column vs column.

## Out of scope

- Parser translation of `Math.*` (TASK-857).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
