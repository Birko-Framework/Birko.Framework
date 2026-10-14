---
id: TASK-615
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-613]
blocks: [TASK-616]
related: [TASK-613]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch stores sort on the CLR property name, so every `orderBy` fails with 400

## Context

Discovered 2026-10-14 while reproducing TASK-613 on a live ES 7.17.25. `ElasticSearchWorkflowInstanceStore.FindByWorkflowNameAsync`
threw before any filter was evaluated:

```
query_shard_exception: No mapping found for [UpdatedAt] in order to sort on
```

`AsyncElasticSearchStore.cs:466`, `ElasticSearchStore.cs:498` and `ElasticSearchViewStore.cs:134` build
`new FieldSort { Field = field.PropertyName }` from `OrderBy<T>`. That name is the CLR member name (`UpdatedAt`).
NEST's default field-name inferrer, which this framework does not override (`ElasticSearch.cs:74`), writes the
document as `updatedAt`. So any `orderBy` on these stores names a field that does not exist. A sort on a `string`
member would also need its `.keyword` sub-field, because ES cannot sort on `text`. Every workflow `Find*` method uses
an `orderBy`, so all of them throw.

## Acceptance criteria

- [x] Sorts resolve the member to its stored field through the same producer the filter translator uses (TASK-613):
      the attribute's `Name` or the camelCase name, plus `.keyword` for a dynamically mapped string.
- [x] Live ES test: `orderBy` ascending and descending on a `DateTime`, an `int` and a `string` member returns rows in
      order, on the sync, async and view stores. Red first.
- [x] Live: the workflow store's `FindByWorkflowNameAsync` / `FindByStateAsync` / `FindByStatusAsync` return the saved
      instances (`ElasticSearchWorkflowInstanceStoreLiveTests`, added at TASK-613, is red until this lands).
- [x] An `orderBy` naming a member `T` does not have (`OrderBy<T>.ByName`) refuses rather than sending an unmapped sort.

## Out of scope

- The filter translator (TASK-613).

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

1. `ElasticSearch.Sorts<T>(OrderBy<T>)`: resolve each key to a property of `T` (by member name, then stored field name,
   then a unique case-insensitive member name) and emit `ExactFieldName` (TASK-613's producer).
   `ArgumentException` for an unknown key, `NotSupportedException` for a `[Text]` member.
2. Replace the five hand-built `FieldSort` loops (async ×2, sync ×2, view store) with it.
3. Tests: offline field names and refusals; live sorts on the sync, async and view stores; the workflow `Find*` test.

## Progress log

- 2026-10-14 — Live ES 7.17.25. Red: sync and async stores "No mapping found for [At]"; view store 400 on `[Rank]`;
  workflow `Find*` "[UpdatedAt]". Green: Data.ElasticSearch 228, Views 20, Workflow.ES 9 incl. the live `Find*` test,
  Sync.ES 12, Migrations.ES 26, ViewModel 6, BackgroundJobs.ES 6. Proving job dequeue live exposed TASK-616 (the claim
  re-read is stale, so a dequeue strands every job it claims and returns none). I filed it with the measurement and did
  not fix it here. Its live test is held for that task.
