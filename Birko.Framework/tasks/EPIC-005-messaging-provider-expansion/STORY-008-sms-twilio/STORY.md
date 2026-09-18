---
id: STORY-008
parent: EPIC-005
status: planned
created: 2026-05-28
---

# SMS via Twilio

## User story

As a developer, I want SMS notifications via Twilio so I can deliver 2FA codes, alerts, and reminders to users.

## Behaviour

- New `ISmsSender` abstraction in `Birko.Messaging` (if not yet present)
- Twilio implementation calls the Twilio REST API
- Delivery status callbacks routed through Birko's event pipeline
- Templated body support (Razor or string template)
