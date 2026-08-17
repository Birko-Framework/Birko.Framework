---
id: TASK-237
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-232, TASK-236]
findings: []
pr: "Birko.BackgroundJobs d6817dc · Birko.BackgroundJobs.Tests 91e80e7 · Birko.BackgroundJobs.Redis.Tests 35219b0 · Birko.BackgroundJobs.SQL.Tests 36117ec"
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

- [x] `RecurringJobScheduler` optionally takes an `IJobLockProvider`. **Absent means today's behaviour
      exactly** — every instance schedules — so this is additive and no existing consumer changes.
      Optional trailing parameters, so the two shipped consumer call sites (`Symbio`, `DraCode`) still
      compile untouched; pinned by `Without_a_lock_provider_both_schedulers_still_enqueue`
- [x] With a provider, only the lock holder enqueues. A non-leader must not merely skip enqueueing while
      still advancing `NextRunAt`, or it will fire immediately on becoming leader — that is a distinct
      duplicate-work bug and the easy one to write by accident.
      **Neither half of that pairing is what shipped**, and the criterion's own wording had them the wrong
      way round: *not* advancing is what leaves `NextRunAt` in the past, so a follower that simply skipped
      would fire every missed occurrence the instant it took over. A follower therefore touches nothing,
      and **the new leader re-baselines** (`NextRunAt = now + interval`) on the transition
- [x] Leadership is **re-attempted**, not decided once at startup — rate-limited by
      `leadershipRetryInterval` (default 15s), because `SqlJobLockProvider` opens a real connection per
      attempt. A failed knock returns false rather than propagating: a follower that faulted out of the
      loop would stop re-attempting, which is worse than the duplication
- [x] On a `IsLeaseBased` provider the loop tolerates losing the lock mid-run — leadership is re-read every
      tick *and* between definitions, so nothing is enqueued once the loss is known
- [x] Red→green proven against **more than one process's worth of schedulers**. Un-wiring leadership:
      **5 of 86** offline, **3 of 20** Redis, **3 of 25** PostgreSQL. Removing only the re-baseline:
      **1 of 86** — the one test that isolates it
- [x] Verified on a **session** provider (PostgreSQL 16) *and* a **lease** provider (Redis 7), both live in
      Docker. SQLite could not stand in: `SqlJobLockProvider` returns `false` there by design, so both
      schedulers would be followers and "one enqueue, not two" would pass while nothing was scheduled
- [x] `Birko.BackgroundJobs/CLAUDE.md` records the wiring, the four rules and that absence of a provider
      means unchanged behaviour; `README.md` gains a "Running more than one worker" section

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

### Production — `Birko.BackgroundJobs/Processing/RecurringJobScheduler.cs`

1. Constructor grows three **optional trailing parameters**: `IJobLockProvider? lockProvider = null`,
   `string lockName = DefaultLockName`, `TimeSpan? leadershipRetryInterval = null`. Source-compatible, so
   `new RecurringJobScheduler(queue, clock)` still means today's behaviour exactly.
2. `RunAsync` asks `EnsureLeadershipAsync` once per tick and skips the whole enqueue pass when it is not
   the leader. **Leadership is never cached across ticks** — `IsLocked` is the provider's own belief and a
   lease-based provider clears it on its own when a renewal finds the key gone.
3. **A new leader re-baselines the schedule** (`NextRunAt = now + interval` for every definition) instead
   of firing every occurrence that elapsed while it was a follower. That is the distinct duplicate-work
   bug the acceptance criteria warn about, and it is the reason a follower must not touch `NextRunAt` at
   all: mutating schedule state from a decision it was not allowed to make is one refactor away from
   enqueueing too.
4. Re-attempt is **rate-limited, not once-only** — `leadershipRetryInterval` (default 15s) bounds how often
   a follower knocks, because `SqlJobLockProvider` opens and closes a real connection per attempt.
5. A failed attempt **returns false rather than propagating** — a follower that throws out of the loop stops
   re-attempting, which is precisely the failure this task exists to prevent.
6. `RunAsync` releases the lock in a `finally`, with `CancellationToken.None`: the loop exits *because* its
   token was cancelled, and both providers' `ReleaseAsync` start with `ThrowIfCancellationRequested`.
7. `IsLeader` exposed for observability and for asserting the follower half in tests.

### Tests

- `Birko.BackgroundJobs.Tests` — an in-process `IJobLockProvider` double giving real cross-instance
  exclusion, so the two-scheduler proof runs offline. Covers: one enqueue not two, no provider means
  unchanged behaviour, the follower takes over when the leader releases, the new leader does not replay,
  and a lease lost mid-run stops enqueueing.
- `Birko.BackgroundJobs.Redis.Tests` — the same two-scheduler assertion on the **lease** provider, gated on
  `BIRKO_REDIS_HOST`.
- `Birko.BackgroundJobs.SQL.Tests` — the same on the **session** provider, gated on `BIRKO_PG_HOST`;
  needs the PostgreSQL projitems import added to that test project.

### Docs

`Birko.BackgroundJobs/CLAUDE.md` — replace "nothing consumes the interface yet" with the wiring, the
re-baseline rule, and that absence of a provider is unchanged behaviour.

## Outcome

**166 tests green across 9 job suites** (86 offline core, 20 Redis, 25 SQL, plus the six backend suites at
36). Three reverts measured rather than asserted — see the acceptance list above for the splits.

Four things worth carrying past this task:

- **The criterion's own reasoning was inverted, and following it literally would have shipped the bug it
  warned about.** It paired "skips enqueueing but still advances `NextRunAt`" with "fires immediately on
  becoming leader"; those are opposite halves. Advancing keeps a follower in phase; *not* advancing is what
  leaves it overdue. The right answer was neither — a follower makes no scheduling decision at all, and the
  new leader re-baselines, because it cannot know what the previous one enqueued. **Re-derive a filed
  criterion's mechanism before implementing to it.**
- **A test that cannot fail was written and caught in review, not by the runner.** The first version of
  `Cancelling_a_coordinated_loop_completes_it_rather_than_faulting_it` cancelled the loop from outside —
  which the loop's own `Task.Delay` observes essentially every time, so it passed with and without the fix.
  The path is only deterministically reachable if the *provider* cancels during acquire. Same family as this
  epic's recurring finding: **ask whether a green check could ever have gone red.**
- **The catch had to be narrowed as well as added.** Swallowing every `OperationCanceledException` would
  have ended the loop on a cancellation belonging to someone else's timeout — permanently stopping
  scheduling over a transient. `when (cancellationToken.IsCancellationRequested)` separates ours from
  everyone's; a foreign one is just a knock that did not land.
- **`Birko.BackgroundJobs.SQL.Tests` was structurally unable to test its own lock provider.** It imports
  only SQLite, where `SqlJobLockProvider` returns `false` by design — so every lock assertion it could make
  was about the *absence* of a lock. The PostgreSQL projitems import added here is what makes the session
  half testable at all.

### Spawned / follow-ups

- **Symbio duplicates this in `RecurringSchedulerLeader` (its TASK-383), and its election is
  once-at-startup.** `TryBecomeLeaderAsync` is called once; a follower never knocks again, so the death of
  Symbio's leader stops all twelve recurring jobs on every node until a restart — the exact failure the
  third acceptance criterion here exists to prevent. Symbio can now delete that class and pass its
  `IJobLockProvider` to the framework scheduler instead. **Not filed in Symbio's tracker**: that repo is
  off-limits this session (large uncommitted work), so this is recorded here for the user to route.
- The idempotency/unique-key approach remains the better long-term answer and remains out of scope — a
  queue-contract change across eight backends.
