---
id: TASK-675
parent: null
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
findings: [FIELD-049]
pr: null
github-issue: null
jira-key: null
---

# `SmtpEmailSender` ignores its own timeout, and a caller cannot tell a transient failure from a permanent one

## Context

Found in the field by Symbio. Recorded in Symbio's CLAUDE.md § TASK-445 and § TASK-561 and in its
`tasks/_loose/TASK-630-no-test-smtp-server-so-no-delivery-path-is-proven-end-to-end.md`. Symbio works
around both problems below in its own `NotificationDeliveryService`. Every other consumer of
`Birko.Messaging` gets neither fix.

**1. The deadline does not hold.** `SmtpEmailSender`'s constructor
(`Birko.Messaging/Email/SmtpEmailSender.cs:28-34`) sets `SmtpClient.Timeout = settings.Timeout`, and
sends with `_client.SendMailAsync`. .NET applies `SmtpClient.Timeout` to the synchronous `Send` only.
Symbio measured `Timeout = 2000` taking **4096 ms**. A hung host can stall the send much longer, and
Symbio once saw a send inside a transaction hold a SQLite write lock for the full 30 s command
timeout (§ TASK-561). `EmailSettings.Timeout` promises a deadline the sender does not keep. Symbio
holds it with a linked `CancellationTokenSource` instead.

**2. Every failure looks the same.** `SendAsync` catches `SmtpException` (line 90) and returns
`MessageResult.Failed($"SMTP error: {ex.Message}", ex)`. `SmtpException` covers both a host that
refused or dropped the connection (transient, worth retrying) and a rejected recipient or mailbox
(permanent). The difference is in `SmtpFailedRecipientException` / `StatusCode` and the **inner**
exception. A caller retrying on `!Success` retries a bad address forever, and Symbio had to
reclassify from the inner exception itself (§ TASK-445). `MessageResult` has no field to carry the
answer: only `Success`, `Error` and `Exception`.

**A timeout is not the caller's cancellation.** `OperationCanceledException` is rethrown today (line
94), which is right for the caller's own token. A send abandoned at the sender's deadline is a
transport failure the caller should see as a failed, retryable result, not as its own cancellation.

## Acceptance criteria

- [ ] `SendAsync` is abandoned at `EmailSettings.Timeout`, async path included. A test with a send
      delegate that never completes (the existing `_sendMail` test seam) returns within the deadline
      plus a small margin, and goes red if the linked deadline is removed.
- [ ] A send that hits the sender's deadline returns a failed result marked transient. A cancellation
      of the **caller's** token still throws `OperationCanceledException`. Both have a test.
- [ ] `MessageResult` tells a caller whether a failure is worth retrying, without parsing `Error` text.
      The shape is decided in the task (e.g. a `MessageFailureKind` of Transient / Permanent / Unknown),
      and is something the other senders (SMS, push, API email providers) can use too.
- [ ] Classification is decided from the SMTP status code / failed-recipient exception / inner
      exception, never from `SmtpException` alone. Tests cover at least: connection refused →
      transient, mailbox unavailable (`SmtpFailedRecipientException`, 5xx) → permanent, `InvalidRecipientException` → permanent.
- [ ] `SendBatchAsync` gets the same deadline and classification per message.
- [ ] Docs (`Birko.Messaging/CLAUDE.md`, `README.md`, `docs/`) say what `Timeout` bounds and how a
      caller decides whether to retry. The Recent Updates entry names Symbio as able to drop its
      linked-CTS and inner-exception workarounds.

## Out of scope

- A real SMTP server for end-to-end tests. That is Symbio TASK-630, on the consumer side.
- API-based email providers (STORY-007), and SMS / push senders.
- Symbio removing its workarounds. That happens in Symbio after this lands.

## Human test plan

N/A — covered by automated tests through the `_sendMail` test seam.

## Implementation plan

_Populated by `/tasks plan TASK-675` — leave empty until then._
