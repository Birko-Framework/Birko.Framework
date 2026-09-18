---
id: TASK-016
feature: FEATURE-006
parent: STORY-012
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-024]
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.MessageQueue.Azure

## Context

Azure Service Bus implementation of `IMessageQueue`. Dependencies: `Birko.MessageQueue`, `Azure.Messaging.ServiceBus`.

## Acceptance criteria

- [ ] `Birko.MessageQueue.Azure` shared project exists, registered everywhere
- [ ] `AzureServiceBusMessageQueue` implements `IMessageQueue`
- [ ] Queues + topics + subscriptions + sessions
- [ ] Dead-letter handling
- [ ] Managed-identity auth supported
- [ ] Settings descendant with connection string or fully-qualified namespace
- [ ] xUnit tests (Azure SDK emulator where available, otherwise mocked client)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Service Bus Premium-only features without a clear consumer need
