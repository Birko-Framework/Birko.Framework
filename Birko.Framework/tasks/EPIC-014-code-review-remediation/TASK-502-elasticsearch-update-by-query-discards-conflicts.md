---
id: TASK-502
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: [TASK-498]
findings: [FIELD-015]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.ElasticSearch]
---

# Elasticsearch's native `PropertyUpdate` discards the UpdateByQuery response, so a version conflict is silent

Found at TASK-498's close gate (intent review); pre-existing, not introduced by it.

## Context

`Birko.Data.ElasticSearch/Stores/{Async,}ElasticSearchStore.cs` `Update(filter, PropertyUpdate<T>)` sends
`UpdateByQueryRequest` with the default `conflicts: abort` and ignores the response. When a matching document changes
between the snapshot and the scripted write, Elasticsearch aborts the request, the remaining documents are not
updated, and the response's `VersionConflicts` / `Failures` carry the evidence — which nobody reads. The call returns
normally: rule 22, *a write that cannot be applied must never report success*.

TASK-498 made this sharper: an increment is exactly the write concurrency collides with, and its XML-doc and the
Stores README now name this task as the reason ES increments are not yet atomic.

## Acceptance criteria

- [ ] A conflicted or failed UpdateByQuery (sync and async) throws, carrying the conflict/failure counts — or retries
      with `conflicts: proceed` plus a bounded re-run of the conflicted documents; decide in the plan, on measurement.
- [ ] The same response check covers `Delete(filter)` (DeleteByQuery) if it has the same shape.
- [ ] The TASK-498 caveat in `Birko.Data.Stores/PropertyUpdate.cs` and `Birko.Data.Stores/README.md` is updated to what
      is now true.
- [ ] Measured against a live Elasticsearch if one is available; otherwise say so, per the family's live-suite rule.

## Out of scope

- The null-as-`""` script parameter (TASK-501).

## Implementation plan

_Populated by `/tasks plan TASK-502` — leave empty until then._
