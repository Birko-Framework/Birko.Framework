---
id: TASK-015
parent: STORY-011
status: todo
priority: P1
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-023]
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.MessageQueue.Kafka

## Context

Apache Kafka implementation of `IMessageQueue`. Dependencies: `Birko.MessageQueue`, `Confluent.Kafka`.

## Acceptance criteria

- [ ] `Birko.MessageQueue.Kafka` shared project exists, registered everywhere
- [ ] `KafkaMessageQueue` implements `IMessageQueue`
- [ ] Topics, partitions, consumer groups, offset management
- [ ] Idempotent producer support
- [ ] Schema-registry integration (optional, behind a flag)
- [ ] Settings descendant of `RemoteSettings`
- [ ] xUnit tests with Testcontainers Kafka
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Kafka Connect / KSQL integrations
- Exactly-once transactional producer (potential follow-up)
