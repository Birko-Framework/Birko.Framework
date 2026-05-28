---
id: TASK-012
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

# Implement Birko.Messaging.Firebase

## Context

Firebase Cloud Messaging (FCM) push notification provider. Introduces an `IPushSender` abstraction in `Birko.Messaging` if not already present. Dependencies: `FirebaseAdmin`.

## Acceptance criteria

- [ ] `IPushSender` abstraction exists in `Birko.Messaging` (added if missing)
- [ ] `Birko.Messaging.Firebase` shared project exists, registered everywhere
- [ ] `FirebasePushSender` implements `IPushSender`
- [ ] Topic + device-token addressing
- [ ] Data vs notification message shape support
- [ ] Settings descendant with service-account JSON path / contents
- [ ] xUnit tests (mocked FirebaseAdmin)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Firebase Remote Config / Analytics
