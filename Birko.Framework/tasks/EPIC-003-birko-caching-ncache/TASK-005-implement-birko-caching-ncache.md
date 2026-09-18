---
id: TASK-005
feature: FEATURE-003
parent: EPIC-003
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

# Implement Birko.Caching.NCache

## Context

NCache distributed cache implementation mirroring the existing Memory / Redis / Hybrid providers. Dependencies: `Birko.Caching`, `Alachisoft.NCache.Client`.

## Acceptance criteria

- [ ] `Birko.Caching.NCache` shared project exists, registered everywhere
- [ ] `NCacheCache` implements `ICache` (Get / Set / Remove / Exists, TTL, generic types)
- [ ] Settings descendant of `RemoteSettings` with NCache cluster config
- [ ] xUnit + FluentAssertions tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Distributed cache invalidation strategies beyond TTL
- L1/L2 hybrid (already covered by `Birko.Caching.Hybrid`)
