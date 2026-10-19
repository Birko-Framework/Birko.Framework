---
id: TASK-676
parent: STORY-009
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Messaging.WebPush — VAPID Web Push to browsers behind `IPushSender`

## Context

`Birko.Messaging` defines `IPushSender` and `PushMessage` (`Birko.Messaging/Push/`) and ships no
implementation. STORY-009 plans **native** push: Firebase (TASK-012) and APNs (TASK-013). Neither
reaches a browser.

**Symbio needs browser push.** Symbio `tasks/EPIC-029-pwa-offline-sync/STORY-064-web-push/TASK-152-web-push-notifications.md`:
"Server can send a VAPID Web Push (backend endpoint) that reaches the SW with the tab closed". That
task puts native FCM/APNs **out of scope**. Symbio's realtime today is SSE/WebSocket, which delivers
only while a tab is open. Its `NotificationDeliveryService` already fans templates out to a push
channel, and that channel fails every send with "Channel '…' not configured — no provider available".

**What Web Push is, server side.**
- **Sending:** an HTTP POST to the subscription's push-service endpoint.
- **Payload encryption:** RFC 8291 (`aes128gcm`, ECDH P-256 with the subscription's `p256dh` key and `auth` secret).
- **Authentication:** a VAPID JWT (RFC 8292), signed ES256 with the server's key pair.
- **Responses to handle:** 201 means delivered to the push service. 404 / 410 mean the subscription
  is gone, and the caller must forget it. 413 means the payload is too large. 429 carries a `Retry-After`.

The browser half (service worker, `PushManager.subscribe`) lives in Birko.Web / the consumer, not
here.

**The shape question.** `PushMessage` is addressed by recipient strings. A Web Push recipient is a
subscription (endpoint + `p256dh` + `auth`), not a device token. Decide how a subscription is
carried without breaking the native senders: a serialized subscription as the recipient, or a
Web Push-specific overload.

## Acceptance criteria

- [ ] New shared project `Birko.Messaging.WebPush`, scaffolded per `new-birko-subproject`, and
      registered in `.slnx`, `.code-workspace` and the aggregator `.csproj`.
- [ ] A `WebPushSender : IPushSender` that sends an RFC 8291-encrypted payload with a VAPID (RFC 8292)
      `Authorization` header. VAPID keys and the subject come from a settings class in the
      framework's Settings chain.
- [ ] Prefer .NET's own crypto (`ECDiffieHellman`, `AesGcm`, `ECDsa`) over a third-party package.
      If a package is used, the task records why and the dependency audit lists it.
- [ ] Encryption verified against the RFC 8291 test vectors (fixed keys and salt → the RFC's exact
      ciphertext). A wrong key turns that test red.
- [ ] A gone subscription (404 / 410) comes back as a distinct, permanent failure, so a caller knows
      to delete it. 429 and 5xx come back as transient. If [[TASK-675]] has landed, use its failure
      classification; otherwise use the same shape.
- [ ] A key-pair helper generates VAPID keys and formats the public key the way a browser's
      `applicationServerKey` expects (uncompressed P-256, base64url).
- [ ] The decision on how a subscription is addressed through `PushMessage` is recorded here, and
      the native senders (TASK-012, TASK-013) are not constrained by it.
- [ ] Tests with a fake push endpoint (an `HttpMessageHandler`): headers (`TTL`, `Content-Encoding`,
      `Authorization: vapid t=…, k=…`), status mapping, payload size limit. Docs and README updated.

## Out of scope

- The service worker, the subscription UI and storing subscriptions. Those are Symbio TASK-152, plus
  a possible Birko.Web.Core helper as a separate task.
- Native push: TASK-012 (Firebase), TASK-013 (APNs).
- Deciding which events warrant a push. That is consumer configuration.

## Human test plan

- [ ] Subscribe Chrome on desktop from a test page, close the tab, send with `WebPushSender`: the
      notification appears. Repeat on Firefox, whose push service is a different provider. Real push
      services cannot be reached from the automated suite.

## Implementation plan

_Populated by `/tasks plan TASK-676` — leave empty until then._
