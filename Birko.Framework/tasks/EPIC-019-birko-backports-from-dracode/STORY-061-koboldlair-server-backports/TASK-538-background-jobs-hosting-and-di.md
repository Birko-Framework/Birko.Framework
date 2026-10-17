---
id: TASK-538
parent: STORY-061
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.BackgroundJobs: hosted-service bridges and DI registration

## Context

`BackgroundJobProcessor` says it is "designed to be hosted as an IHostedService", but nothing in `Birko.BackgroundJobs`
references `IHostedService` or `IServiceCollection`, so every consumer writes the bridge:

- DraCode: `Jobs/BackgroundJobProcessorHostedService.cs` (29), `Jobs/RecurringJobSchedulerHostedService.cs` (23), and
  the wiring in `Program.cs` L683–708
- Symbio: `JobProcessorHostedService.cs` (36), `RecurringJobHostedService.cs` (89), `JobExtensions.cs` (118) — with
  lessons already paid for: the idempotency guard is keyed on the processor, not `IJobQueue` (Symbio TASK-383);
  `AddRecurringJob<TJob>` applies to the scheduler that actually runs (Symbio TASK-457); a follower must still run its loop

Precedent for the shape: `Birko.EventBus.Outbox/Hosting/OutboxProcessorHostedService.cs`.

Adopted in the consumer by DraCode TASK-136 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] Hosted services for the processor and the recurring scheduler (a `Hosting/` folder, or `Birko.BackgroundJobs.Hosting` if the dependency graph wants the split)
- [x] `AddBackgroundJobs(...)` / `AddRecurringJob<TJob>(...)`, idempotent, with Symbio's three lessons as tests
- [x] Graceful stop honours the host's shutdown token
- [x] Start from Symbio's version (it carries the fixes), check it against DraCode's wiring

## Human test plan

N/A — covered by tests.

## Resolution (2026-10-18)

**New `Birko.BackgroundJobs.Hosting`** (its own project: `Birko.BackgroundJobs` stays free of hosting packages, and
BardStudio imports the core). Started from Symbio's version and checked against DraCode's wiring.
- `AddBackgroundJobs(configure)`: clock, in-memory queue, `ScopedJobExecutor`, dispatcher, processor and recurring
  scheduler, each kept when already registered, plus the two hosted services. Idempotent, keyed on the processor.
- `AddRecurringJob<TJob>(name, interval, queueName)`: a `RecurringJobRegistration`, applied to the running scheduler
  at startup.
- `LoopHostedService` base, `BackgroundJobProcessorHostedService`, `RecurringJobSchedulerHostedService`.
- Options: `Queue` (`JobQueueOptions`), `RecurringLockName` (Symbio passes `symbio:recurring-scheduler`),
  `LeadershipRetryInterval`.

**Symbio's three lessons are tests, each mutation-checked against its defect:**
1. The guard keys on the processor (TASK-383). Mutated to key on `IJobQueue`: red.
2. A recurring job reaches the running scheduler in either order (TASK-457). Tested both ways, with a real tick.
3. A follower runs its loop and takes over (TASK-457). Mutated with Symbio's old start-time gate: red.

**Beyond Symbio's version:**
- Each job resolves from its own DI scope (`ScopedJobExecutor`). DraCode resolved from the root provider, so a scoped
  dependency failed under `ValidateScopes`; with DraCode's executor as a mutation, the test is red.
- `StopAsync` waits only until the host's shutdown token fires. A mutation awaiting the loop unbounded was red (34 s).
- The token source is disposed only after the loop ends; Symbio disposed it while the loop could still be using it.
- A faulted loop is logged.

Not ported: Symbio's `RecurringJobSlotGuard` (its own executor decorator, which it registers before calling).

Note for the processor triage ([[TASK-158]]): `BackgroundJobProcessor.Stop()` cancels the token its running jobs were
given, so a stop is never purely graceful. That behaviour is documented here and left to the triage.

**Tests:** `BackgroundJobsHostingTests`, 9 tests against a real `ServiceProvider` (`ValidateScopes`), all green. The
Sandbox aggregator builds. Docs: the stale processor and scheduler snippets in `docs/background-jobs.md` were fixed
(`StartAsync` does not exist, and the constructors were wrong), and a Hosting section was added. The spec
`background-jobs` gained the requirement.
