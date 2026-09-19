---
id: EPIC-005
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Messaging.SendGrid, Birko.Messaging.Mailgun, Birko.Messaging.Twilio, Birko.Messaging.Firebase, Birko.Messaging.Apple, Birko.Messaging.Brevo, Birko.Messaging.Mailjet]
---

# Birko.Messaging — Provider expansion

## Area of concern

Add cloud messaging providers beyond the existing SMTP + Razor template stack — SendGrid + Mailgun for email, Twilio for SMS, Firebase Cloud Messaging + APNs for push notifications.

Added 2026-09-19: **dual-channel providers**, whose single API serves email *and* SMS — Brevo ([[TASK-471]])
and Mailjet ([[TASK-472]]). They are **epic-direct**, not under a story, because the stories below split by
channel and a dual-channel provider belongs to two of them at once; splitting one per channel would
duplicate the client, settings and error mapping. Their scope decision is `D4` in
[FEATURE-005's ledger](../../docs/features/FEATURE-005-messaging-provider-expansion/decisions.md) and is
still `proposed` — the tasks were filed ahead of it.

## Success criteria

- Seven sibling shared projects exist and are registered (five channel-specific, two dual-channel)
- Each implements the appropriate `IEmailSender` / `ISmsSender` / `IPushSender` abstraction — a dual-channel provider implements two
- DI extensions wire each into the host's messaging pipeline
- Basic send-success tests pass against provider mocks
