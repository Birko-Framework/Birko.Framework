---
id: TASK-616
parent: EPIC-014
feature: null
status: todo
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

- [ ] The live test passes: two jobs on one queue are dequeued one per call, highest priority first. It is red today
      (measured).
- [ ] The claim is verified against state that includes its own write: a realtime GET by id, or a refresh on the
      claiming `update_by_query`. Decide which on the cost per dequeue, and record the reason.
- [ ] A dequeue that does not return a job leaves no job it touched in Processing. Live test.
- [ ] The class comment about stale re-reads is corrected to what was measured.

## Out of scope

- TASK-502 (`update_by_query` conflicts discarded) is its own finding. Note here if this fix closes it.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
