---
id: TASK-502
parent: EPIC-014
feature: null
status: done
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

> **Measured at [[TASK-319]] (2026-10-11), on the reindex response only:** NEST's `IsValid` already goes false when
> the body lists `failures`, but **not** on `timed_out: true` — an HTTP 200 with no failures and only part of the work
> done. The by-query responses share the body shape; measure whether their `IsValid` behaves the same before relying
> on it, and check `TimedOut` explicitly either way (`ReindexHelper.Refusal` is the shape used there).

## Acceptance criteria

- [x] A conflicted or failed UpdateByQuery (sync and async) throws, carrying the conflict/failure counts — or retries
      with `conflicts: proceed` plus a bounded re-run of the conflicted documents; decide in the plan, on measurement.
      → **Throw.** A conflict throws `StoreConcurrencyException` (`Applied`, `Conflicts`); any other failure, or a
      timeout, throws `StoreException`. Retrying is wrong for the one caller that relies on conflicts, the job claim,
      where a conflict means another worker won. It also cannot be made exact for an increment without per-document
      bookkeeping, because the abort leaves an unknown remainder unprocessed. The job queue now catches the
      concurrency exception as a lost race.
- [x] The same response check covers `Delete(filter)` (DeleteByQuery) if it has the same shape.
- [x] The TASK-498 caveat in `Birko.Data.Stores/PropertyUpdate.cs` and `Birko.Data.Stores/README.md` is updated to what
      is now true.
- [x] Measured against a live Elasticsearch if one is available; otherwise say so, per the family's live-suite rule.
      → ES 7.17.25 (Docker). Conflict: 409, `IsValid=false`, ~330 applied / ~670 conflicts / the rest untouched. Script
      error: 400. Delete-by-query: same shape. `TimedOut` false throughout, but checked on its own anyway (TASK-319).

## Out of scope

- The null-as-`""` script parameter (TASK-501).

## Implementation plan

1. Measure the by-query response shapes (throwaway probe): conflict, script error, delete-by-query.
2. `ElasticSearchStoreHelper.RequireComplete(response, index)`, used by the sync and async `Update(filter, …)` and
   `Delete(filter)`. New `Birko.Data.Exceptions.StoreConcurrencyException : StoreException`.
3. The job queue's claim catches `StoreConcurrencyException` and tries the next candidate.
4. Live tests: an exact invariant over concurrent increments (sync and async), a delete racing an update, and racing
   dequeuers.

## Progress log

- 2026-10-14 — Red with the stores reverted: three "successful" increments left 1,000 of 9,000 (sync and async), and a
  "successful" delete left 2,432 rows. Racing dequeuers without the queue's catch: `StoreConcurrencyException` in 3
  of 3 runs. Green: Data.ElasticSearch 232, BackgroundJobs.ES 8 (racing test 3 of 3), Views 20, Workflow.ES 9,
  Sync.ES 12, Migrations.ES 26, ViewModel 6, Data.Core 108.
