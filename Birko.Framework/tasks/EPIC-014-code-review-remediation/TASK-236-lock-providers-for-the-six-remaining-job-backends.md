---
id: TASK-236
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-232, TASK-237]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A per-backend verdict on locking for the six job backends without a provider

## Context

Split out of [[TASK-232]], which was filed as this question and turned out to have a prior one underneath:
**the two existing providers did not implement the same contract.** That is now fixed — `acquireTimeout`
and `leaseDuration` are separate parameters, `IsLeaseBased` is exposed, and Redis renews its lease on a
heartbeat. This task is the question TASK-232 originally asked, now worth answering because there is a
sound contract to answer it against.

**Adding implementations first would have been the wrong order.** Four more providers built against a
contract whose two existing implementations disagreed would have multiplied the inconsistency rather than
closed a gap — and every new one would have had to pick, silently, which of the two meanings of `timeout`
it honoured.

| Backend | Job queue | Lock provider | Plausible primitive |
|---|---|---|---|
| CosmosDB | `CosmosDBJobQueue` | — | etag / conditional write, TTL |
| ElasticSearch | `ElasticSearchJobQueue` | — | optimistic concurrency (`if_seq_no`), TTL |
| MongoDB | `MongoDBJobQueue` | — | `findAndModify` + TTL index |
| RavenDB | `RavenDBJobQueue` | — | **compare-exchange** — designed for distributed locks |
| JSON | `JsonJobQueue` | — | OS file lock |
| XML | `XmlJobQueue` | — | OS file lock |

## The question is now narrower, and one row may surprise

Every candidate above except the file stores can offer **only a lease**, because none has a server-side
notion of "release this when that client disappears". So each would be `IsLeaseBased == true` and would
need the same renewal heartbeat Redis now has — which means the interesting question per backend is not
"can it lock" but **"is a renewed lease worth the machinery here, given the queue is already atomic on
dequeue"**.

**`JSON` and `XML` may be the inverted case, and it is worth measuring rather than assuming.** TASK-232's
first sketch listed them as "probably not supported" on the grounds that file locking is unportable. But an
OS file lock is released by the kernel when the holding process dies — which is **genuine session
semantics**, the stronger guarantee, and the one thing none of the document stores can offer. Whether .NET
exposes that portably enough (`FileShare.None` on a lock file, and what happens on a network share) is the
thing to measure. Do not inherit the earlier guess.

## Acceptance criteria

- [ ] A per-backend verdict for all six: *implement* (naming the primitive and the `IsLeaseBased` value) or
      *record as unsupported* (naming what the backend cannot provide). A recorded "no" is a legitimate
      deliverable
- [ ] Measure the file-store case rather than assuming: does an OS file lock give session semantics
      portably, and does it survive the store's own file handling? If it does, `JSON`/`XML` are the only
      backends besides SQL that can offer a session lock, and the earlier guess was backwards
- [ ] Every implemented provider follows the settled contract: `acquireTimeout` waits, `leaseDuration`
      bounds, `IsLeaseBased` is honest, and a lease-based one **renews on a heartbeat** — an unrenewed
      lease reintroduces exactly the defect TASK-232 fixed
- [ ] Each implemented provider has tests for the behaviours the interface singles out: a loser gets
      `false` rather than an exception, releasing an unheld lock is not an error, and — for lease-based
      ones — **the heartbeat keeps the lock past its original lease**, proven by mutation. That last test
      is the one that matters; the others pass trivially
- [ ] Any provider that cannot be tested without a live service says so, and its gate is checked (a gated
      suite that silently no-ops is how the lock providers went untested in the first place)
- [ ] `Birko.BackgroundJobs/CLAUDE.md`'s support table is updated as each lands

## Out of scope

- The contract itself — [[TASK-232]], landed.
- Wiring leader election into `RecurringJobScheduler` — [[TASK-237]].
- SQLite. It already reports `false` deliberately (CR-M027, after previously returning `true` for a lock it
  never took); giving it a lock would need a separate mechanism and is not what this task is about.

## Human test plan

N/A — acquisition, contention, release and lease renewal are all observable by automated tests, gated on a
live service where one is needed.

## Implementation plan

_Populated by `/tasks plan TASK-236` — leave empty until then._
