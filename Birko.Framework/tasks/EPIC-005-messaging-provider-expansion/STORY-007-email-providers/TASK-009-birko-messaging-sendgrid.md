---
id: TASK-009
parent: STORY-007
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

# Implement Birko.Messaging.SendGrid

## Context

SendGrid email provider implementing `IEmailSender`. Dependencies: `Birko.Messaging`, `SendGrid` SDK.

## Acceptance criteria

- [ ] `Birko.Messaging.SendGrid` shared project exists, registered everywhere
- [ ] `SendGridEmailSender` implements `IEmailSender`
- [ ] Template support via the existing Razor pipeline
- [ ] Attachments, custom headers, tracking parameters supported
- [ ] Settings descendant with API key + sandbox-mode flag
- [ ] xUnit tests (mocked HTTP client)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Inbound parse webhook
- Marketing API (lists, campaigns)
