---
id: TASK-030
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

# Birko.Caching.Redis.Tests

## Context

Deferred test project for the Redis-backed cache. Original reason: Redis infrastructure not available in CI. Now run against Testcontainers Redis.

## Acceptance criteria

- [ ] `Birko.Caching.Redis.Tests` project exists
- [ ] Testcontainers Redis fixture
- [ ] Get / Set / Remove / Exists tests
- [ ] TTL expiration tests
- [ ] Bulk operations
- [ ] Cache-stampede / dog-pile protection if applicable
- [ ] Wired into `Birko.Framework.slnx`

## Out of scope

- Hybrid cache coordination tests (covered by Hybrid.Tests)
