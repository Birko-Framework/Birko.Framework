---
id: TASK-291
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-277, TASK-286, TASK-290]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# `EnsureSchemaAndReport` rewraps a cancellation as a bare `Exception`, so a client that hung up becomes a 500

## Context

`AbstractConnector.EnsureSchemaAndReport` is the shared body of all four providers' `OnException`
handlers, and it rewraps **every** exception:

```csharp
var reported = new Exception(DescribeSchemaEscape(ex, commandText), ex);
RecordSchemaEscape(reported, CreatedTablesNamedIn(commandText).Select(kvp => kvp.Key));
throw reported;
```

`DescribeSchemaEscape` returns the command text unchanged when the failure is not a missing table, so a
`TaskCanceledException` / `OperationCanceledException` arrives at the host as
`Exception("SELECT ...", inner: TaskCanceledException)`. The **type is gone**, so nothing downstream can
tell "the caller went away" from "the database failed" — and a host that maps cancellation to 499 or to a
silent abort cannot select it. It surfaces as an unhandled 500.

Note the paths that reach here already treat cancellation as special *elsewhere*: the SQLite bulk
overloads carry an explicit `catch (OperationCanceledException) { rollback; throw; }` ahead of their
general catch, precisely so a cancellation is not routed through `InitException`. The single-command paths
(`RunCommand`, `RunCommandTransaction`, `RunCommandOn`) have no such arm, so their cancellations do get
rewrapped.

## Evidence, and its honest weight

Observed **six times** on the consumer side, all under Symbio's own 20 s `AbortSignal` during a 567-way
concurrency storm (Symbio TASK-602 round 7). So this is **induced, not field-reported** — the harness
aborted its own requests. It is recorded there as a warning to a future reader, because a grep for 500s
in those cycles finds these six and not the defect that run was actually about:

> WARNING: The "500s served" column of cycles 3 and 5 is NOT this defect and must not be counted as it.
> All six were `TaskCanceledException` — the harness's own 20 s `AbortSignal` firing under 567-way
> concurrency, rewrapped by `EnsureSchemaAndReport` and surfacing as `Unhandled exception`.

That is the real cost: not a broken request (the client had already gone), but a **diagnostic channel
polluted with noise that looks exactly like a server fault**, in the one condition where somebody is
reading the log carefully.

## The shape of the fix, and what it must not do

Let a cancellation through untouched, ahead of the rewrap:

- do not annotate it (`DescribeSchemaEscape` has nothing to say about it),
- do not record it as a schema escape,
- do not bump `SchemaGeneration` — TASK-288's invalidation is gated on the anomaly and a cancellation is
  not one,
- rethrow the original so the type survives.

WARNING: **it must not become a general "let some exceptions through" seam.** TASK-277 exists because these
handlers used to answer a missing table with a `return`, silently discarding a write and reporting success.
The distinction is that a cancellation is *the caller's own decision*, already reported to the caller by
its own token, so there is nothing to report a second time — whereas every database failure must keep
reporting. Any change here needs a test on **both** sides: a cancellation preserves its type and records
nothing, and a missing-table write still throws and still records.

Check the async twin (`AbstractAsyncConnector`) and all four provider handlers, not just the one that is
easy to reach — per this repo's repeated finding that a funnel with four overrides is not a funnel.

## Acceptance

- [ ] A cancelled command's `OperationCanceledException` / `TaskCanceledException` reaches the caller with
      its type intact, from both the sync and async single-command paths.
- [ ] It records nothing on `SchemaEscapes` and does not move `SchemaGeneration`.
- [ ] A missing-table **write** still throws and still records (TASK-277 / TASK-286 / TASK-288 unchanged),
      asserted in the same suite so the change cannot go one-sided.
- [ ] Mutation-proven: removing the cancellation arm reds the cancellation tests and nothing else.
