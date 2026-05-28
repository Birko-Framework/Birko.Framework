---
id: STORY-007
parent: EPIC-005
status: planned
created: 2026-05-28
---

# Email providers (SendGrid + Mailgun)

## User story

As a developer, I want email senders beyond SMTP — SendGrid for production reliability, Mailgun for cost-effective sends — both behind the `IEmailSender` abstraction.

## Behaviour

- Each provider implements `IEmailSender`
- Template support via the existing `Birko.Messaging.Razor` pipeline
- Attachments and tracking metadata supported where the provider offers them
- Bounce / spam / open / click events surfaced through Birko's event pipeline where available
