---
id: TASK-648
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Async alias reindex reports Failed after a completed swap when the old-index delete is cancelled

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `schema-index-and-ddl`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

In `ReindexWithAliasAsync`, an `OperationCanceledException` from the old-index delete skips the cleanup handler and
reaches the outer catch-all (`Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:323,330`): the result is
`Failed` although the alias already points at the new index, and a retry dies at "already exists". `ReindexAsync`'s
catch-all also turns any cancellation into a Failed result (rule 9: a cancellation is the caller's, never rewrapped).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] A completed swap reports success (with CleanupError) whatever happens to the delete
- [x] A cancellation propagates as cancellation
- [x] Red on the current code; the spec `docs/specs/schema-index-and-ddl.md` updated if behaviour changes

## Resolution (2026-10-17)

**Measured live (ElasticSearch 7.17, local Docker), and the mechanism differs from the reading.** A `HttpConnection`
subclass cancelled the caller's token at the moment it saw a chosen request.
- **Before the swap:** cancelling at `_reindex`, `ReindexAsync` and `ReindexWithAliasAsync` both answered a `Failed`
  result. No `OperationCanceledException` reached the caller (2 of 2 red).
- **After the swap:** cancelling the old-index `DELETE` already returned success with a `CleanupError`, but only by
  accident.

The cause is that NEST does not throw on a cancelled request. It returns an invalid response whose
`OriginalException` is the cancellation. `IndexManager.ValidateResponse` rewrapped that as
`InvalidOperationException("Failed to delete index …")`, which the cleanup catch swallowed, and the reindex calls
read it as a refusal. So the defect the task predicted was masked by a worse one, rule 9: a cancellation was never
the caller's.

**Fix:**
- `IndexManager.ThrowIfCancelled(response)` rethrows the response's `OperationCanceledException`, using
  `ExceptionDispatchInfo` to keep the stack. It runs in `ValidateResponse`, in the existence answer, and after both
  async `ReindexOnServer` calls and their refreshes.
- The async catch-alls exclude `OperationCanceledException`.
- `ReindexWithAliasAsync` decides by where the cancellation landed. Before the swap, the new index is deleted with
  `CancellationToken.None` and the cancellation rethrown, so a retry can create it. During the swap request it
  propagates without undoing anything, because the cluster may have applied it. After the swap, a cancelled delete
  is a `CleanupError` ("Cancelled before the old index … was deleted; alias … points at …") on a successful result.
- The sync methods take no token and are unchanged.

**Red check.** With `ThrowIfCancelled` a no-op, the 2 propagation tests failed. With cancellation surfacing but the
original cleanup filter restored (`when (cleanup is not OperationCanceledException)`, no cancellation catch), the
post-swap test failed: that is the defect the task named, reachable once the masking one is fixed.

**Tests:** `Task648ReindexCancellationLiveTests` (3, live). `Birko.Data.ElasticSearch.Tests` is 285 of 285. The 8
suites that import the project (389 tests) are green.

**Docs:** the spec `schema-index-and-ddl` (the ReindexWithAlias requirement; the scenario that recorded the old
behaviour now reads as fixed; a new scenario) and Recent Updates.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-648` — leave empty until then._
