---
id: TASK-828
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB filtered reads and updates can miss documents written just before

Found by [[TASK-826]]'s computed-set contract (2026-11-07).

## Observed

During the full `Birko.Data.RavenDB.Tests` run against the live container (RavenDB 6.0), the async contract created six
documents, ran `UpdateAsync(filter, PropertyUpdate)`, and read back five updated rows. Alone, the same test passed five
runs of five. The RavenDB stores run filtered queries without waiting for non-stale results (no `WaitForNonStaleResults`
or similar anywhere in `Birko.Data.RavenDB/Stores`), so a query can be answered by an index that has not caught up yet.

Not established: whether the update's own read missed the sixth document (the update silently skipped a matching row)
or only the read-back did. Either way a caller can see a write it just made go missing from a filtered read, and a
filtered `Update` / `Delete` (read, then write each match) can leave a matching row untouched.

TASK-826's contract now waits for the indexes in its `Settle` step (`RavenLive.WaitForIndexes`), as the Elasticsearch
contract refreshes, so it tests the formula and not the index timing. This task is about the store.

## Acceptance criteria

- [x] Reproduce: a filtered read or update straight after writes, under load, that misses a document; record which of
      the two (read vs. update) misses.
- [x] Decide with the user: wait for non-stale results on filtered reads and filter-based writes (cost: latency under
      write load), or document the eventual consistency as the provider's contract.
- [x] If fixed: a regression test that fails without the wait.

## Human test plan

N/A — verified by the live RavenDB suites.

## Outcome (2026-11-07)

**What misses.** The update does not. The contract was run in the full suite with its index wait removed and a
diagnostic, 4 runs, each failing. Every time, the update had applied to all 6 documents (read back by id), while a
query on the value it had just written (`Code == "done"`) returned 5. An unfiltered query returned all 6. So the stale
part is a query issued right after a write.

The standalone probes found no misses (none committed): 20 sequential rounds on one database, 48 fresh databases with
4 in parallel, and 2×48 rounds querying the just-written value. It shows only under the suite's load.

**Decision (the user, 2026-11-07): writes wait for the indexes.** New `RavenIndexWait.Apply` sets
`WaitForIndexesAfterSaveChanges(15 s, throwOnTimeout: false)` on every session.

- It is applied to all 27 session openings in the sync and async stores, and to the `RavenDbUnitOfWork` session.
- A timeout does not throw. The write is applied by then, and reporting it as failed would invite a repeat.
- The cost is write latency.

**Tests.**

- `Task828IndexWaitTests` covers sync writes, async writes and a unit-of-work commit. A test-built `DocumentStore`
  records every `/bulk_docs` batch, and each one must carry `waitForIndexesTimeout`. Green with the fix, red 3 of 3
  with the wait disabled.
- Behavioural check: the TASK-826 contract no longer waits for the indexes itself (`RavenLive.WaitForIndexes` is
  removed). The full RavenDB suite passed 3 runs of 3, where the same contract without the store fix failed 4 of 4.

**Regression.** All green: RavenDB 159, BackgroundJobs.RavenDB 24, Localization.RavenDB 11, Migrations.RavenDB 40,
RavenDB.ViewModel 4, RavenDB.Views 11, Sync.RavenDB 5, Workflow.RavenDB 8.

**Not changed.** `RavenDBIndexManager`'s own sessions are index administration, not store writes.
