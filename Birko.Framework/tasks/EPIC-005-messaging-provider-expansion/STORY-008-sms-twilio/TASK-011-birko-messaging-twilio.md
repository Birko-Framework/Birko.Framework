---
id: TASK-011
parent: STORY-008
status: todo
priority: P1
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Messaging.Twilio

## Context

Twilio SMS implementation. Introduces an `ISmsSender` abstraction in `Birko.Messaging` if not already present, then implements it over the Twilio REST API.

## Acceptance criteria

- [ ] `ISmsSender` abstraction exists in `Birko.Messaging` (added if missing)
- [ ] `Birko.Messaging.Twilio` shared project exists, registered everywhere
- [ ] `TwilioSmsSender` implements `ISmsSender`
- [ ] Settings descendant with account SID, auth token, default from-number
- [ ] Templated body via existing Razor / string-template pipeline
- [ ] Delivery status callback support
- [ ] xUnit tests (mocked HTTP client)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Twilio Voice / Video / WhatsApp
