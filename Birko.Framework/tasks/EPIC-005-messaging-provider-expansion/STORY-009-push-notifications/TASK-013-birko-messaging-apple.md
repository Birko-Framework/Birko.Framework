---
id: TASK-013
parent: STORY-009
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

# Implement Birko.Messaging.Apple

## Context

Apple Push Notification service (APNs) push provider using `.p8` token auth. Dependencies: `PushSharp` or a modern equivalent.

## Acceptance criteria

- [ ] `Birko.Messaging.Apple` shared project exists, registered everywhere
- [ ] `ApplePushSender` implements `IPushSender`
- [ ] Settings: key ID, team ID, bundle ID, .p8 path / contents, sandbox vs prod
- [ ] Device-token + topic-based addressing
- [ ] alert / silent / mutable-content payload shapes supported
- [ ] xUnit tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- VoIP push payloads
- Legacy certificate-based auth
