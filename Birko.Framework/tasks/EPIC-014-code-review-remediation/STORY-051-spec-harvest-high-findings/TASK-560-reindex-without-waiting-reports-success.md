---
id: TASK-560
parent: STORY-051
feature: FEATURE-014
status: done
priority: P3
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-027]
pr: null
github-issue: null
jira-key: null
---

# `ReindexHelper.Reindex(…, waitForCompletion: false)` reports `Successful` for a reindex that has only started

## Context

Found while fixing [[TASK-319]] (SH-H038, 2026-10-11). `Reindex` / `ReindexAsync`
(`Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs`) take `waitForCompletion`. With `false`, Elasticsearch
answers at once with a **task id** and no counts; the helper then refreshes the target and returns
`ReindexResult.Successful(…, response.Created /* 0 */, …)` — a success for a copy that has not happened yet, whose
outcome nothing checks. `ReindexResult` has no "started" state and does not carry the task id, so the caller cannot
follow it up either.

Not destructive today: `ReindexWithAlias` always waits, so it never acts on a started-only result. Reach: 0 consumer
references. Hence P3.

## Decision (2026-10-12, by the user)

**Keep `waitForCompletion`, and make a started reindex visibly pending.** `ReindexResult` gains a pending state that
carries Elasticsearch's task id (`response.Task`), so the caller can follow the reindex up; it is never `Successful`,
and the helper does not refresh a target that is still being written. Rejected: dropping the parameter — a breaking
signature change that also removes the only way to run a long reindex in the background.

The task is no longer a decision task: it is runnable by [[fix-next]].

## Acceptance criteria

- [x] Decide: drop the parameter (always wait), or make a started reindex visibly not-a-success — e.g. a result that
      carries `response.Task` and says it is pending, and no refresh of a target still being written
- [x] Tests pin the decision with a canned task-id response (the fake transport in
      `ReindexFailureReportingTests` already answers by path)
- [x] `/specs regen schema-index-and-ddl` — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)

## Out of scope

- The timed-out and per-document-failure outcomes — fixed / pinned by [[TASK-319]].

## Resolution (2026-10-16)

**Decision (the user): a started reindex is reported as pending, not as a success; the parameter stays.**
`Reindex` / `ReindexAsync` with `waitForCompletion: false` return `ReindexResult.Started`: `Success = false`,
`IsPending = true`, and `TaskId` (`node:id`, from `response.Task.FullyQualifiedId`), with a message naming
`GET _tasks/{id}`. The target is no longer refreshed while it is being written. A start answered with no task id is a
failure, not pending. `ReindexWithAlias` is unchanged: it always waits.

**Red-verified:** with the result shape added and the helper unchanged, 3 of the 4 new fake-transport tests failed (the
4th, a completed reindex is not pending, is a pin). **Live** on ElasticSearch 7.17 (`ReindexStartedLiveTests`): the
returned task id follows the real task to a completed 3-document copy. **Regression:** the 8 suites importing
Birko.Data.ElasticSearch (376 tests), live, green; `Racing_dequeuers…` failed once (known flake, no reindex involved)
and passed 3 of 3 reruns.

Fixed on the way: the README's reindex example printed `result.DocumentsIndexed`, which does not exist.

**Spec regen pending**, batched: `schema-index-and-ddl`.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-560` — leave empty until then._
