---
id: TASK-999
parent: EPIC-019
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-998, TASK-701]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: the localized broad-match chunked delete is sometimes cancelled by the server

## Context

Seen while verifying [[TASK-996]] and [[TASK-998]]. In the full RavenDB live suite,
`RavenBroadMatchContractTests.Localized_A_broad_match_on_a_translated_field_is_counted_paged_and_deleted_in_chunks`
failed in 2 of 4 full runs on 2026-11-27, at the same point:

```
Raven.Client.Exceptions.RavenException : System.OperationCanceledException: The operation was canceled.
  at AsyncRavenDBStore`1.DeleteCoreAsync(IEnumerable`1 data, ...)   (SaveChangesAsync)
  at AsyncLocalizedBulkStoreWrapper`3.DeleteWithTranslationsAsync(...)
```

The exception is thrown on the **server** and relayed as a `RavenException`: a large delete in one `SaveChanges` is
cancelled partway, apparently by a server-side timeout under the suite's load. The same test passed in the other 2 full
runs.

**Not known yet:**
- Whether it predates 2026-11-27. The first failure came before TASK-998's retry existed, but after TASK-996's start-up
  change. TASK-996 touches only database creation, not this delete.
- Whether the cancelled `SaveChanges` applied part of its batch. That decides whether a retry is ever safe (rule 66).
  `RavenQueryRetry` deliberately does not retry it.

## Measured 2026-11-27

**Where it fails (server stack, from the failing run).** `BatchHandlerProcessorForBulkDocs.ExecuteInternalAsync` had run
the batch, then `WaitForIndexesAsync` → `Index.IsStale` → `JsonContextPoolBase.AllocateOperationContext` threw
`OperationCanceledException`. That is the wait for indexes after `SaveChanges` that `RavenIndexWait` turns on
([[TASK-828]]), and it runs after the batch is committed. So **the delete was applied, and the store reported it as
failed**. That contradicts TASK-828's own rule: an applied write must not be reported as failed, because a retry
repeats it. TASK-828 guards the wait's timeout (`throwOnTimeout: false`) but not a cancellation.

**Hypotheses for the cancellation, each tested:**

| Hypothesis | Test | Result |
|---|---|---|
| The database was unloaded mid-request | RavenDB server log around the failure | ruled out: the only unloads are other tests' hard deletes of their own databases |
| An auto index was merged and disposed during the wait | probe: index on `Code`, then `Code`+`Name`, then a 3 000-document delete, 8 rounds | 0 of 8 failed |
| The server's own wait timeout leaks as a cancellation | probe: 2 000-document deletes with a 1 ms and a 50 ms wait, 12 rounds each | 0 of 24 failed |
| The test fails by itself | the real test alone, 10 runs | 10 of 10 passed (about 13 s each) |

It fails only inside the full RavenDB suite: 2 of 4 runs on 2026-11-27, both on the same delete. The cause of the
cancellation is not known (rule 54: recorded, not adopted).

## Acceptance criteria

The two criteria below were written before the measurement and assumed a batch-size limit. The server stack showed
no such limit: the batch commits, and only the index wait after it is cancelled. They are answered as follows.

- [x] ~~Reproduce it alone or under a matched shape; measure the batch size at which the server cancels.~~ Not
      reproducible on demand (four hypotheses falsified, above); the stack places it in the post-commit wait, not the
      batch, so there is no batch-size limit to measure. Recorded, not adopted (rule 54).
- [x] Establish whether a cancelled `SaveChanges` applied anything: **yes, all of it**: the cancellation is in
      `WaitForIndexesAsync`, after `ExecuteInternalAsync` committed the batch. So no chunk sizing; the fix is that the
      wait can no longer fail the write.
- [x] A write whose index wait fails is reported as applied: the save is sent without a server-side wait, then a
      separate wait follows that never throws (the user's decision, "Separate the wait"). Regression tests
      `A_write_whose_index_wait_fails_is_reported_as_applied` (async: create, delete, create) and its sync twin fail the
      wait request on purpose; under the old mechanism the wait was inside the save, so the same failure failed the
      write.
- [x] TASK-828's guarantee kept: `Task828IndexWaitTests` asserts every write batch (stores, both sync and async, and
      the unit of work) carries no server-side wait and is followed by the separate wait; the computed-set contract
      (reads its own writes) passes.

## Done 2026-11-27

- `RavenIndexWait.SaveChanges` / `SaveChangesAsync(session, collection, ct)`: the save through `RavenQueryRetry`
  (TASK-998), then the index statistics and an empty waiting query per stale index over the collection (every
  collection for a unit of work), within 15 s; every exception in the wait is swallowed. `Apply` no longer changes a
  session. All 9 async and 7 sync store saves and `RavenDbUnitOfWork.CommitAsync` go through it.
- **Full RavenDB live suite: 469/469 in 4 of 4 runs** (12 m 52 s – 14 m 32 s), against the broad-match delete failing in
  2 of 4 before. Not proof, since the cause of the cancellation is unknown: at the old 50% rate, four clean runs by
  chance is about 6%.
- Cost: each save adds one index-statistics request, plus one waiting query per stale index over the collection.
- Symbio.Birko builds (scratch output).
- Not changed: `Birko.Data.Localization.RavenDB` (`RavenLocalizedWriteBoundary`) and `Birko.Data.Migrations.RavenDB`
  open their own sessions and never used `RavenIndexWait`, before or after.
- Spec drift: `docs/specs/backend-store-implementations.md` and `docs/specs/unit-of-work-and-transactions.md` still
  describe `WaitForIndexesAfterSaveChanges`; they are generated, and update at the next `/specs regen`.

## Out of scope

- The retry policy for refusals that applied nothing ([[TASK-998]]).

## Human test plan

N/A — fully covered by automated tests (live suite).
