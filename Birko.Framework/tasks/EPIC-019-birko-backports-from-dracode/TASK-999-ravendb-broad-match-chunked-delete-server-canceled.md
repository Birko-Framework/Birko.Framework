---
id: TASK-999
parent: EPIC-019
feature: null
status: in-progress
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

- [ ] Reproduce it, alone or under a matched concurrency shape (rule 11). Measure the batch size and duration at which
      the server cancels, and check it against a commit before 2026-11-27.
- [ ] Establish whether a cancelled `SaveChanges` applied anything. Then either size the delete chunks under the
      server's limit, or document the limit, with a regression test that fails first.

## Out of scope

- The retry policy for refusals that applied nothing ([[TASK-998]]).

## Human test plan

N/A — fully covered by automated tests (live suite).
