---
id: TASK-615
parent: EPIC-014
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-613]
blocks: []
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

- [ ] Sorts resolve the member to its stored field through the same producer the filter translator uses (TASK-613):
      the attribute's `Name` or the camelCase name, plus `.keyword` for a dynamically mapped string.
- [ ] Live ES test: `orderBy` ascending and descending on a `DateTime`, an `int` and a `string` member returns rows in
      order, on the sync, async and view stores. Red first.
- [ ] Live: the workflow store's `FindByWorkflowNameAsync` / `FindByStateAsync` / `FindByStatusAsync` return the saved
      instances (`ElasticSearchWorkflowInstanceStoreLiveTests`, added at TASK-613, is red until this lands).
- [ ] An `orderBy` naming a member `T` does not have (`OrderBy<T>.ByName`) refuses rather than sending an unmapped sort.

## Out of scope

- The filter translator (TASK-613).

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

_Drafted when picked._
