# Birko.BackgroundJobs.Redis.Tests

## Overview
Unit tests for Birko.BackgroundJobs.Redis — the Redis-backed job queue.

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.BackgroundJobs.Redis.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- Closes the CR-H010 "no tests" gap. `RedisJobQueueScoreTests` reflection-tests the pure scoring
  math for CR-C02: due jobs are eligible, future/delayed jobs (even high priority) are not, and
  higher priority dequeues first among ready jobs — so the score and the dequeue threshold agree.
- **No live server.** The Lua ZRANGEBYSCORE dequeue path needs a running Redis instance (infra gap).
