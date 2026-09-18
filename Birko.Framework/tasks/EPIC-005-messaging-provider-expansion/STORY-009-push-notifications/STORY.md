---
id: STORY-009
parent: EPIC-005
status: planned
created: 2026-05-28
---

# Push notifications (Firebase + APNs)

## User story

As a developer, I want push notifications on Android (FCM) and iOS (APNs) behind a unified `IPushSender` abstraction.

## Behaviour

- New `IPushSender` abstraction in `Birko.Messaging`
- Firebase implementation uses `FirebaseAdmin`
- APNs implementation uses `.p8` token auth (PushSharp or modern equivalent)
- Topic / device-token addressing
- Per-platform payload shaping (data vs notification messages)
