---
id: TASK-237
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-232, TASK-236]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `RecurringJobScheduler` duplicates every job per worker — wire leader election

## Context

Split out of [[TASK-232]], whose third decision was *whether anything in `Birko.BackgroundJobs` should
consume `IJobLockProvider`*. **Decided 2026-08-17 by the user: yes, as leader election (option 3a).** This
task is that implementation.

**The defect is concrete.** `RecurringJobScheduler` holds its schedule in a `ConcurrentDictionary` in
process memory, and `NextRunAt` is a field on that in-memory object:

```csharp
if (now >= def.NextRunAt)
{
    await _queue.EnqueueAsync(descriptor, cancellationToken);
    def.NextRunAt = now.Add(def.Interval);   // per-process state
}
```

Run three workers and each has its own dictionary and its own `NextRunAt`. At the due moment **all three
enqueue**. Three copies of every recurring job, on every backend, including the two that have a lock —
because nothing consumes it. `IJobLockProvider` appears in exactly three files today: its declaration and
the two implementations.

## Why leader election and not a lock per decision

Both shapes were considered. The rejected one is worth recording, because it looks like the obvious answer.

**Per-decision locking** — take a lock named for the due instant (`recurring:cleanup:2026-08-17T10:00`) and
let only the winner enqueue — **does not work with a lock**. Each process releases immediately after
enqueueing, so a process whose loop or clock is a few seconds behind arrives later, finds the lock free, and
enqueues a duplicate. To close that you would have to hold the lock until the *next* due instant, at which
point it is not a lock at all: it is a persistent record saying "10:00 was already enqueued".

That is the insight worth keeping: **"has this occurrence already been enqueued?" is an idempotency
question, not a mutual-exclusion one.** Its right answer is a unique key on the queue (job name + due
instant), which every durable backend can enforce and which would work on all eight rather than the two
that can express a lock. Recorded here as the better long-term shape; not this task's scope.

**Leader election** is what a session lock is actually for: one process acquires `recurring-scheduler` and
holds it for as long as it lives, and only the leader runs the loop.

## Acceptance criteria

- [ ] `RecurringJobScheduler` optionally takes an `IJobLockProvider`. **Absent means today's behaviour
      exactly** — every instance schedules — so this is additive and no existing consumer changes
- [ ] With a provider, only the lock holder enqueues. A non-leader must not merely skip enqueueing while
      still advancing `NextRunAt`, or it will fire immediately on becoming leader — that is a distinct
      duplicate-work bug and the easy one to write by accident
- [ ] Leadership is **re-attempted**, not decided once at startup. A process that loses the race must keep
      trying, or the death of the leader leaves nothing scheduling until every worker restarts
- [ ] On a `IsLeaseBased` provider the loop tolerates losing the lock mid-run: `IsLocked` can go false on
      its own (Redis sets it when a renewal finds the key gone), and the scheduler must stop enqueueing
      rather than carry on believing it leads
- [ ] Red→green proven against **more than one process's worth of schedulers** — two scheduler instances
      sharing one lock provider backend, asserting one enqueue rather than two. Reverting the wiring must
      turn that test red; a single-instance test proves nothing here
- [ ] Verified on a **session** provider (SQL) *and* a **lease** provider (Redis), because their failure
      modes are opposite: a stuck session lock blocks handover forever, while an expired lease lets two
      leaders coexist
- [ ] `Birko.BackgroundJobs/CLAUDE.md` records the wiring and that absence of a provider means unchanged
      behaviour

## Out of scope

- The unique-key/idempotency approach discussed above. Strictly better in coverage, and a queue-contract
  change across eight backends — its own decision, not a side effect of this one.
- Lock providers for the six backends that have none — [[TASK-236]]. This task must work when the provider
  is absent, so it does not depend on that.
- Making jobs themselves idempotent. The interface is explicit that a lock *"reduces duplication, it does
  not abolish it"*; that remains the consumer's responsibility.

## Human test plan

N/A — "two schedulers, one enqueue" is observable by an automated test against a real backend.

## Implementation plan

_Populated by `/tasks plan TASK-237` — leave empty until then._
