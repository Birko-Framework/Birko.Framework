---
id: TASK-828
parent: null
feature: null
status: todo
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

- [ ] Reproduce: a filtered read or update straight after writes, under load, that misses a document; record which of
      the two (read vs. update) misses.
- [ ] Decide with the user: wait for non-stale results on filtered reads and filter-based writes (cost: latency under
      write load), or document the eventual consistency as the provider's contract.
- [ ] If fixed: a regression test that fails without the wait.

## Human test plan

N/A — verified by the live RavenDB suites.
