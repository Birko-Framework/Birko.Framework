---
id: TASK-560
parent: STORY-051
feature: FEATURE-014
status: todo
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

## Acceptance criteria

- [ ] Decide: drop the parameter (always wait), or make a started reindex visibly not-a-success — e.g. a result that
      carries `response.Task` and says it is pending, and no refresh of a target still being written
- [ ] Tests pin the decision with a canned task-id response (the fake transport in
      `ReindexFailureReportingTests` already answers by path)
- [ ] `/specs regen schema-index-and-ddl`

## Out of scope

- The timed-out and per-document-failure outcomes — fixed / pinned by [[TASK-319]].

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-560` — leave empty until then._
