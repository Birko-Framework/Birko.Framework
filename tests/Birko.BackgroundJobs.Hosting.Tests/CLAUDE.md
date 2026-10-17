# Birko.BackgroundJobs.Hosting.Tests

## Overview
Tests of `AddBackgroundJobs`, `AddRecurringJob` and the hosted services (TASK-538), started and stopped by hand from a
real `ServiceProvider` (`ValidateScopes`). Symbio's three lessons are tests: a durable queue registered first plus a
repeated call yields one processor, a recurring job reaches the running scheduler in either call order, and a
follower runs its loop and takes over. They also cover a scoped job dependency resolved and disposed per job, a stop
bounded by the shutdown token while a job ignores cancellation, a prompt idle stop, kept registrations and argument
checks. Each lesson was mutation-checked against the defect it guards.

## Dependencies
xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection / Hosting.Abstractions / Logging.Abstractions;
imports Birko.Time, Birko.Serialization, Birko.BackgroundJobs, Birko.BackgroundJobs.Hosting.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
