---
id: TASK-868
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-867]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB's index manager and aggregation helpers skip the null-ordering fold

## Context

Found by TASK-867 (2026-11-08), by reading the code; not reproduced.
- TASK-867 added `NullOrderingFold` and called it in every store and view translator.
- `RavenDBIndexManager` (around lines 358, 398, 419) and the aggregation helpers pass filters to the driver without
  the fold.
- RavenDB matches `Score > null` against every non-null Score (measured in TASK-867), so those paths would return wrong
  rows silently.
- `RavenViewStore` now runs `RavenFilterRewriter` (which brings `Contains` → `In` and the normalizer), but that side
  effect has no test of its own.

## Acceptance criteria

- [ ] Reproduce first (rule 54): each filter-taking path that bypasses `RavenFilterRewriter` (index manager, aggregation
      helpers, and any other found by grep), with `x.N > nul`, `!(x.N > nul) && …`, live against the C# oracle. Do the
      same grep for every other provider; record any path that skips the fold.
- [ ] Each one runs the fold (`NullOrderingFold`), one producer. Where a path is fed an already-rewritten filter,
      record that instead.
- [ ] Tests red before the fix, plus a test for `RavenViewStore`'s `Contains` → `In` rewrite.

## Out of scope

- The store and view translators (TASK-867, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
