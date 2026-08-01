---
id: TASK-017
feature: FEATURE-006
parent: STORY-012
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-025]
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.MessageQueue.Aws

## Context

AWS SQS implementation of `IMessageQueue` (standard + FIFO queues). Dependencies: `Birko.MessageQueue`, `AWSSDK.SQS`.

## Acceptance criteria

- [ ] `Birko.MessageQueue.Aws` shared project exists, registered everywhere
- [ ] `SqsMessageQueue` implements `IMessageQueue`
- [ ] Standard + FIFO queues with message groups and dedup IDs
- [ ] Long polling on receive
- [ ] Dead-letter queue redrive policy
- [ ] xUnit tests against LocalStack
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- SNS fan-out (could pair as a follow-up `Birko.MessageBus.Sns`)
