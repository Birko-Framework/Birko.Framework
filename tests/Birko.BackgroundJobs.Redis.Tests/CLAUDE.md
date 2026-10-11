# Birko.BackgroundJobs.Redis.Tests

## Overview
Unit tests for Birko.BackgroundJobs.Redis — the Redis-backed job queue.

## Project Location
`tests/Birko.BackgroundJobs.Redis.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- Closes the CR-H010 "no tests" gap. `RedisJobQueueScoreTests` reflection-tests the pure scoring
  math for CR-C02: due jobs are eligible, future/delayed jobs (even high priority) are not, and
  higher priority dequeues first among ready jobs — so the score and the dequeue threshold agree.
- **Live dequeue tests are gated.** `RedisUnnamedJobDequeueTests` (TASK-561) exercises the Lua dequeue script
  against a real server when `BIRKO_REDIS_HOST` is set (a reported skip otherwise; a failure under
  `BIRKO_REQUIRE_LIVE`). Locally: `docker run -d --rm -p 127.0.0.1:6379:6379 redis:7`.
