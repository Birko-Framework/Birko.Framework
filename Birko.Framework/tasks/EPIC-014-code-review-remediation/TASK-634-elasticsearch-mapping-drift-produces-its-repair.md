---
id: TASK-634
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-621, TASK-624, TASK-633]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch mapping drift produces its repair, not only the report

## Context

Filed 2026-10-15 with the user, as the ElasticSearch twin of [[TASK-633]] (SQL `PlanRepair`). [[TASK-621]] gave the
ES stores `DetectMappingDrift()` (`Missing`, `Unexpected`, `TypeMismatch`) and [[TASK-624]] a health check over it, but
like SQL drift before TASK-633 it only reports. A `Missing` field is not cosmetic: a filter on a field the mapping lacks
matches nothing, silently (measured in TASK-621, a `[Keyword]` member over a `text` index).

The repairs split the way SQL's did:

- **Missing**: additive and in place. A `PUT _mapping` for the field, built from the store's own index descriptor (the
  one it creates the index with, so the repair cannot disagree with a freshly created index). Existing documents are
  untouched; whether they need a reindex to become searchable on the new field is part of what to measure.
- **TypeMismatch**: ElasticSearch cannot change a field's type in place. The only repair is a new index, `_reindex`
  and an alias swap (`ReindexHelper` exists). It moves data, so the plan names it and does not run it, as SQL leaves a
  type change to the operator.
- **Unexpected**: a field no model maps (a removed property, an older dynamic sub-field). Listed, never removed.

Also in scope, because it is the other half of "do the NoSQL providers need this": **measure** that a document lacking a
newly added property deserializes to the property's default on MongoDB and RavenDB (no column schema, so no DDL is
needed there; the claim is the reason they get no repair plan, and it is unmeasured).

## Acceptance criteria

- [ ] A plan from an ES store's drift: the `PUT _mapping` body for each `Missing` field, the reindex named (not run) for
      each `TypeMismatch`, `Unexpected` listed; shape mirrors `SchemaRepairPlan` (statements / not actioned).
- [ ] Live against ElasticSearch: an index created for an older model gains the new field from the plan, a re-run of
      `DetectMappingDrift` reports nothing, a filter on the new field finds a document indexed after the repair, and the
      behaviour of documents indexed before it is measured and documented.
- [ ] Live on MongoDB and RavenDB: a document stored before a model gained a non-nullable property reads back with the
      default (or the measured failure is filed).
- [ ] Mutation-prove the mapping body (a wrong field type in the plan must fail the re-check).
- [ ] CHANGELOG; docs.

## Out of scope

- Running a reindex automatically.
- Index drift on MongoDB / RavenDB / CosmosDB: [[TASK-635]].

## Human test plan

N/A: asserted against live ElasticSearch, MongoDB and RavenDB.
