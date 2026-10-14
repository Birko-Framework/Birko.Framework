---
id: TASK-616
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-14
depends-on: [TASK-615]
blocks: []
related: [TASK-613, TASK-615, TASK-502]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch job dequeue claims jobs, re-reads them stale, and returns none

## Context

Measured 2026-10-14 on a live ES 7.17.25 while proving TASK-615. The test is
`tests/Birko.BackgroundJobs.ElasticSearch.Tests/ElasticSearchJobQueueLiveTests.cs`: it was written there and left
uncommitted for this task.

`ElasticSearchJobQueue.DequeueAsync` (`ElasticSearchJobQueue.cs:84-114`) claims a candidate with a filtered
`UpdateAsync` (`update_by_query`). It then re-reads it with `ReadAsync(j => j.Guid == claimId)`, a **search**, and
accepts the claim only if the re-read shows its `ClaimToken`. Nothing refreshes the index between the two, and a search
sees a write only after a refresh (~1 s), so the re-read returns the pre-claim document and the token never matches.
The loop then claims the next candidate the same way.

Measured: two jobs on queue `mail`, one `DequeueAsync("mail")`. It returned **null**, and afterwards **both** jobs read
`status: 2` (Processing) with claim tokens and `attemptCount: 1`. The queue claims every eligible job up to
`MaxClaimAttempts`, hands out none, and strands them in Processing until stale-job recovery, if any, resets them. The
class comment calls the re-read "briefly stale" and narrows the double-dispatch window. In practice the staleness is
the normal case, not an edge.

The `update_by_query` response is also discarded (TASK-502), so a version conflict is invisible here.

## Acceptance criteria

- [x] The live test passes: two jobs on one queue are dequeued one per call, highest priority first. It is red today
      (measured).
- [x] The claim is verified against state that includes its own write: a realtime GET by id, or a refresh on the
      claiming `update_by_query`. Decide which on the cost per dequeue, and record the reason.
      → A realtime GET, as `AsyncElasticSearchStore.ReadRealtimeAsync` (public wrapper with lazy-init over a
      virtual `ReadRealtimeCoreAsync`). A refresh per claim would force a segment refresh on the whole index for every
      dequeue on every worker. A GET reads one document from the translog and costs nothing beyond that. The store
      method also lets the in-memory double take part, and gives other read-your-write callers the same tool.
- [x] A dequeue that does not return a job leaves no job it touched in Processing. Live test.
      → The live test asserts the job left behind is still Pending after the other is handed out, and that a third
      dequeue on the drained queue returns null.
- [x] The class comment about stale re-reads is corrected to what was measured.

## Out of scope

- TASK-502 (`update_by_query` conflicts discarded) is its own finding. Note here if this fix closes it.
  → It does not close it. The claim no longer depends on the conflict count, because the realtime read-back tells a
  lost race apart, but `UpdateAsync(filter, …)` still discards the response for every other caller.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. `AsyncElasticSearchStore.ReadRealtimeAsync(guid)` / `ReadRealtimeCoreAsync`: a realtime GET by document id. Not
   found is null; any other failure throws.
2. The job queue's claim read-back uses it. The in-memory double overrides the core method.
3. Tests: the live dequeue test (red first, measured twice), a live store-level realtime read, and the offline queue
   suite unchanged.

## Progress log

- 2026-10-14 — Red: live `DequeueAsync("mail")` returned null with both mail jobs left in Processing (dumped
  `_source`); re-proved after the fix by reverting only the queue file. Green: BackgroundJobs.ES 7/7 (incl. the live
  test), Data.ElasticSearch 229 (incl. `ReadRealtimeTests`), Views 20, Workflow.ES 9, Sync.ES 12, Migrations.ES 26,
  ViewModel 6. A first attempt put the GET inside the queue; it broke the 5 offline tests that use the in-memory
  double, which is why the read lives on the store as a seam.
