---
id: EPIC-005
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Messaging.SendGrid, Birko.Messaging.Mailgun, Birko.Messaging.Twilio, Birko.Messaging.Firebase, Birko.Messaging.Apple]
---

# Birko.Messaging — Provider expansion

## Area of concern

Add cloud messaging providers beyond the existing SMTP + Razor template stack — SendGrid + Mailgun for email, Twilio for SMS, Firebase Cloud Messaging + APNs for push notifications.

## Success criteria

- Five sibling shared projects exist and are registered
- Each implements the appropriate `IEmailSender` / `ISmsSender` / `IPushSender` abstraction
- DI extensions wire each into the host's messaging pipeline
- Basic send-success tests pass against provider mocks
