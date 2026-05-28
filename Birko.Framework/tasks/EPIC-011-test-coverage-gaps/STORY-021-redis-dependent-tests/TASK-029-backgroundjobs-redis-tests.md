---
id: TASK-029
parent: STORY-021
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.BackgroundJobs.Redis.Tests

## Context

Deferred test project for the Redis-backed BackgroundJobs store. Original reason: Redis infrastructure not available in CI. Now run against Testcontainers Redis.

## Acceptance criteria

- [ ] `Birko.BackgroundJobs.Redis.Tests` project exists
- [ ] Testcontainers Redis fixture (xUnit collection)
- [ ] CRUD on JobDescriptorModel
- [ ] Job enqueue / dequeue / ack / fail flows
- [ ] Retry policy behaviour
- [ ] FluentAssertions assertions throughout
- [ ] Wired into `Birko.Framework.slnx`

## Out of scope

- Performance/load tests
