---
id: TASK-896
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-885]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB store reads give wrong rows for `HasValue`, negated orderings over null and `string.Compare`

## Context

Found by TASK-885 (2026-11-10), measured on the emulator against C#, 34 filter shapes through the store's LINQ provider
(reads, and so also the guarded writes and the aggregate filter since TASK-876):
- `x.Score.HasValue` matches every row (6 of 6 where C# matches 4). Silently wrong.
- `!(x.Score > 20)` misses the null rows (C#: `!(null > 20)` is true). The view translator (`CosmosFilterTranslator`) has
  the same defect. TASK-867's fold covers orderings against a null *constant*, not a null column under NOT.
- `string.Compare(x.Code, "c2") > 0` misses a row.
- `string.IsNullOrEmpty` is refused by the store (the view translator answers it).
This also blocks giving `CosmosViewStore` the store's translator (one producer), which TASK-885 kept apart on purpose.

## Acceptance criteria

- [ ] Reproduce (rule 54) on reads, counts, the writes the guard allows and aggregates, store and view.
- [ ] Each wrong shape is translated exactly (a pre-pass rewrite, as `NullOrderingFold` does) or refused by name.
- [ ] Decide on measurement whether the view store can then use the store's translator; do it or record why not.
- [ ] Tests, red before the fix.

## Out of scope

- Aggregates themselves (TASK-885, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
