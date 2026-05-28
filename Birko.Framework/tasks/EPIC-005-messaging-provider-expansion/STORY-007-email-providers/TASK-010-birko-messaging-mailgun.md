---
id: TASK-010
parent: STORY-007
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

# Implement Birko.Messaging.Mailgun

## Context

Mailgun email provider implementing `IEmailSender`. Cost-effective alternative to SendGrid. Dependencies: `Birko.Messaging`, Mailgun .NET client.

## Acceptance criteria

- [ ] `Birko.Messaging.Mailgun` shared project exists, registered everywhere
- [ ] `MailgunEmailSender` implements `IEmailSender`
- [ ] Domain + region (US / EU) configurable via settings
- [ ] Attachments + tags supported
- [ ] xUnit tests (mocked HTTP client)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Mailgun inbound routing webhooks
- Mailing list API
