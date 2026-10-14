---
id: TASK-613
parent: EPIC-014
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: [TASK-615]
related: [TASK-610]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch filter translator queries `<field>.keyword` on fields that are mapped as `keyword` themselves

## Context

Found 2026-10-14 by the cross-provider audit spawned from TASK-610 (same class: the name the framework emits is not
the name the backend stores). Source read and confirmed; the empty-result consequence is **not measured yet**.

- `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:829-833` (and the script path near :563) appends `.keyword`
  to every `string` member in a term query.
- `Birko.Workflow.ElasticSearch/Models/ElasticWorkflowInstanceModel.cs` declares `[Keyword(Name = "workflowName")]`
  and `currentState`. `Birko.BackgroundJobs.ElasticSearch/Models/ElasticJobDescriptorModel.cs` does the same for
  `jobType`, `queueName` and `claimToken`. Indexes are created with `AutoMap()`, which maps these as plain `keyword`
  fields with **no `.keyword` sub-field**.
- So `FindByStateAsync` / `FindByStatusAsync` / `FindByWorkflowNameAsync`
  (`ElasticSearchWorkflowInstanceStore.cs:77,90,102`) would query `workflowName.keyword`, a field that does not exist,
  and match 0 documents without an error. That is a silently empty result (rule 48's tell). The audit suspects
  `Birko.Data.Sync.ElasticSearch`'s `ElasticSyncKnowledgeItem` (`scope`, `entityGuid`) too, but did not check its
  query path.

## Acceptance criteria

- [x] Live ES test (gate `BIRKO_ES_URL`): a workflow instance saved through the ES workflow store is found by
      `FindByWorkflowNameAsync` / `FindByStateAsync`. Measure red first. If it is already green, record why and
      cancel. → It was red, but because of a different bug: every `orderBy` sends the PascalCase name and gets a 400,
      before any filter runs. That bug is TASK-615. This task's half is proven at the store level
      (`MappedFieldNameLiveTests`) and through the sync store, and the workflow-level test lands with TASK-615.
- [x] The translator emits `.keyword` only where the mapping has that sub-field. It decides from the mapping or the
      member's attributes, not from the CLR type alone. A `text` field with a `.keyword` sub-field (the default dynamic
      mapping) still gets the suffix.
- [x] The same check for the BackgroundJobs and Sync ES models, each with a test or a recorded reason it is unaffected.
      → Sync: `GetLastSyncTimeAsync` filters `Scope` (`[Keyword]`). It answered null ("never synced") and is now
      correct; proven live in both directions (`ElasticSyncKnowledgeScopeLiveTests`). Jobs: dequeue filters
      `QueueName` (`[Keyword]`), so it is affected and now translates correctly, but it also sorts, so its live proof
      belongs to TASK-615.

## Out of scope

- Index and alias name casing, which the audit read as consistently lower-cased.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. One producer: `ElasticSearch.StoredFieldName(member, prefix)` (the attribute's `Name`, else camelCase) and
   `ExactFieldName(member, type, prefix)`. `.keyword` is added only for an unattributed string, nothing for
   `[Keyword]`, and any other string mapping is refused.
2. Route the four member sites through it: the bare bool, `HasValue`, the direct member, and the script field.
3. Tests: offline translator tests, a live store round-trip and a live sync scope lookup, each proven red first.

## Progress log

- 2026-10-14 — Live ES 7.17.25 (Docker). Red: `store.Read(x => x.Code == "A-1")` returned an empty collection;
  without the fix the sync `GetLastSyncTimeAsync` returned null. The workflow repro exposed TASK-615 (sort 400),
  which I filed and did not fix here. Green: Data.ElasticSearch 222 incl. 6 new, Views 19, ViewModel 6,
  Migrations.ES 26, Sync.ES 12, BackgroundJobs.ES 6.
