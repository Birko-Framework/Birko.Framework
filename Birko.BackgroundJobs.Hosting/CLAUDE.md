# Birko.BackgroundJobs.Hosting

## Overview
Runs Birko.BackgroundJobs inside a .NET host (TASK-538): `AddBackgroundJobs` registers the services, and two hosted
services run the processor and the recurring scheduler. It is backported from Symbio's `JobExtensions` and hosted
services, keeping the three lessons Symbio paid for (each one a test), and replaces DraCode's two hosted services and
its `Program.cs` wiring.

## Project Location
`C:\Source\Birko\Framework\Birko.BackgroundJobs.Hosting\`

## Components
- `BackgroundJobsHostingExtensions.AddBackgroundJobs(configure)`
  - Registers, keeping anything already registered: `IDateTimeProvider` (system clock), `IJobQueue` (in-memory),
    `IJobExecutor` (`ScopedJobExecutor`), `JobDispatcher`, `BackgroundJobProcessor` (with `options.Queue`), and
    `RecurringJobScheduler` (with the registered `IJobLockProvider`, `RecurringLockName` and `LeadershipRetryInterval`).
  - Also registers the two hosted services.
  - **Idempotent, keyed on the processor** (Symbio TASK-383): a host registers its durable queue before the modules
    load, and a guard keyed on the queue left a queue with no processor.
- `AddRecurringJob<TJob>(name, interval, queueName)` registers the job (transient) and a `RecurringJobRegistration`.
  The registration is applied to the scheduler that runs, when the host starts, in any call order (Symbio TASK-457:
  registering on a scheduler from `BuildServiceProvider()` registered on a throwaway).
- `LoopHostedService`, the base of the two hosted services:
  - The loop runs on its own token.
  - `StopAsync` waits only until the host's shutdown token fires.
  - The token source is disposed once the loop has ended, and a faulted loop is logged. Symbio disposed it while the
    loop could still be using it, and observed no fault.
- `BackgroundJobProcessorHostedService`: stopping the processor also cancels its running jobs' tokens, so "graceful"
  means the jobs observe that and the host waits up to its timeout.
- `RecurringJobSchedulerHostedService` applies the registrations, logs when no lock provider is registered, and
  **always starts the loop, also on a follower** (Symbio TASK-457). Leadership is the scheduler's, re-attempted every
  tick; a gate here meant no node scheduled anything after the leader died.
- `ScopedJobExecutor` resolves each job from its own DI scope and disposes it after the job. DraCode resolved jobs from
  the root provider, so a scoped dependency failed under `ValidateScopes`.

⚠ `RecurringLockName` must be the same on every node of an application, across versions; two names during a rolling
deploy are two leaders.

## Dependencies
- Birko.BackgroundJobs, Birko.Time.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Hosting.Abstractions,
  Microsoft.Extensions.Logging.Abstractions (provided by an ASP.NET Core or generic host)

Its own project so `Birko.BackgroundJobs` stays free of hosting packages (BardStudio imports the core).

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.BackgroundJobs.Hosting.Tests`.
