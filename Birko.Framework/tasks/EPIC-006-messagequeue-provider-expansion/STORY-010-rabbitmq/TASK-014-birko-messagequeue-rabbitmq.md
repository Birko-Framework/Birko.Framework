---
id: TASK-014
feature: FEATURE-006
parent: STORY-010
status: todo
priority: P1
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-022]
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.MessageQueue.RabbitMQ

## Context

RabbitMQ (AMQP) implementation of `IMessageQueue`. Dependencies: `Birko.MessageQueue`, `RabbitMQ.Client`.

## Acceptance criteria

- [ ] `Birko.MessageQueue.RabbitMQ` shared project exists, registered everywhere
- [ ] `RabbitMqMessageQueue` implements `IMessageQueue`
- [ ] Exchanges, queues, publisher confirms, consumer ack / nack
- [ ] Connection recovery on transient failures
- [ ] Dead-letter exchange support
- [ ] Settings descendant of `RemoteSettings`
- [ ] xUnit tests with Testcontainers RabbitMQ
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- RabbitMQ Streams (newer feature; potential follow-up)
- RPC pattern beyond standard Birko abstractions
