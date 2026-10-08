---
id: TASK-538
parent: STORY-061
feature: null
status: todo
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

- [ ] Hosted services for the processor and the recurring scheduler (a `Hosting/` folder, or `Birko.BackgroundJobs.Hosting` if the dependency graph wants the split)
- [ ] `AddBackgroundJobs(...)` / `AddRecurringJob<TJob>(...)`, idempotent, with Symbio's three lessons as tests
- [ ] Graceful stop honours the host's shutdown token
- [ ] Start from Symbio's version (it carries the fixes), check it against DraCode's wiring

## Human test plan

N/A — covered by tests.

## Implementation plan
