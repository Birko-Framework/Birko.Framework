---
id: TASK-018
parent: STORY-013
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

# Implement Birko.MessageQueue.MassTransit

## Context

Thin adapter implementing `IMessageQueue` over MassTransit's `IBus`. Lets teams already using MassTransit consume Birko abstractions without duplicating broker setup.

## Acceptance criteria

- [ ] `Birko.MessageQueue.MassTransit` shared project exists, registered everywhere
- [ ] `MassTransitMessageQueue` implements `IMessageQueue` by delegating to `IBus`
- [ ] Doesn't expose MassTransit middleware concepts upward — they stay configured on MassTransit
- [ ] Settings supports either passing in an existing `IBusControl` or registering a configurator
- [ ] xUnit tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Reimplementing MassTransit features (sagas, courier, etc.)
