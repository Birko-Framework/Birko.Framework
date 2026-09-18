---
id: TASK-033
feature: FEATURE-011
parent: STORY-022
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

# Birko.Configuration + Birko.Contracts DTO tests

## Context

Lightweight DTO-shape tests on `Birko.Configuration` and `Birko.Contracts`. Mostly POCOs, so the surface is small but worth pinning to catch accidental rename / removal that would break consumers.

## Acceptance criteria

- [ ] `Birko.Configuration.Tests` / `Birko.Contracts.Tests` exist
- [ ] Settings hierarchy chain instantiation (ISettings → Settings → PasswordSettings → RemoteSettings → SqlSettings → typed descendants)
- [ ] Contract interface presence (ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
- [ ] Serialization round-trip for representative types
- [ ] Wired into `Birko.Framework.slnx`

## Out of scope

- Heavy integration with downstream consumers (covered elsewhere)
