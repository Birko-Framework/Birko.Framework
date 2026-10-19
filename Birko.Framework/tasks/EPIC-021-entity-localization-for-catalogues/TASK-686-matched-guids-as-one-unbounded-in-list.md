---
id: TASK-686
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-19
depends-on: [TASK-681]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-L087]
pr: null
github-issue: null
jira-key: null
---

# A filter on a translated field reaches the entity store as one unbounded `IN` list

## Context

Spawned from [[TASK-681]] (decided by the user, 2026-10-19), which took SH-L087 over from [[TASK-194]]. A filter on a
translated field is resolved against the translation store first; the matching entity GUIDs then reach the entity
store as one `IN` list (`LocalizedFilterHelper.BuildGuidFilter`, and since TASK-681 also a `NOT IN` list of base-value
candidates that have a translation). A broad match — a `Contains` over a large catalogue — pushes the list past MSSQL's
~2 100 parameters (and MySQL's packet size), so the read fails at the driver. Nothing in the framework chunks `IN` lists,
and the wrappers cannot join across two stores (the translations may live in another one).

## Acceptance criteria

- [ ] Above a set size (the translation reads already batch at `LocalizedTranslationRows.ReadBatchSize`), the entity
      query runs in chunks and the results are merged: reads concatenate, `Count` sums, and ordering / paging that
      cannot be pushed into each chunk is applied in memory, as the localized-sort path already does
- [ ] Filter-based `Update` and `Delete` apply chunk by chunk with the same coverage
- [ ] A live MSSQL test with a match over 2 100 entities passes, red with the chunking removed

## Out of scope

- The translation-side reads — batched by [[TASK-679]].

## Human test plan

N/A — asserted by the live suite.

## Implementation plan

_Populated by `/tasks plan TASK-686` — leave empty until then._
