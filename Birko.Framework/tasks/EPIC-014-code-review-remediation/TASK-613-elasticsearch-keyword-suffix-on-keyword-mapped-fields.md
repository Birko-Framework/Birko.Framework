---
id: TASK-613
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
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

- [ ] Live ES test (gate `BIRKO_ES_URL`): a workflow instance saved through the ES workflow store is found by
      `FindByWorkflowNameAsync` / `FindByStateAsync`. Measure red first. If it is already green, record why and
      cancel.
- [ ] The translator emits `.keyword` only where the mapping has that sub-field. It decides from the mapping or the
      member's attributes, not from the CLR type alone. A `text` field with a `.keyword` sub-field (the default dynamic
      mapping) still gets the suffix.
- [ ] The same check for the BackgroundJobs and Sync ES models, each with a test or a recorded reason it is unaffected.

## Out of scope

- Index and alias name casing, which the audit read as consistently lower-cased.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
