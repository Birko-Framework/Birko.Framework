---
id: TASK-675
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] `SendAsync` is abandoned at `EmailSettings.Timeout`, async path included. A test with a send
      delegate that never completes (the existing `_sendMail` test seam) returns within the deadline
      plus a small margin, and goes red if the linked deadline is removed.
- [x] A send that hits the sender's deadline returns a failed result marked transient. A cancellation
      of the **caller's** token still throws `OperationCanceledException`. Both have a test.
- [x] `MessageResult` tells a caller whether a failure is worth retrying, without parsing `Error` text.
      The shape is decided in the task (e.g. a `MessageFailureKind` of Transient / Permanent / Unknown),
      and is something the other senders (SMS, push, API email providers) can use too.
- [x] Classification is decided from the SMTP status code / failed-recipient exception / inner
      exception, never from `SmtpException` alone. Tests cover at least: connection refused →
      transient, mailbox unavailable (`SmtpFailedRecipientException`, 5xx) → permanent, `InvalidRecipientException` → permanent.
- [x] `SendBatchAsync` gets the same deadline and classification per message.
- [x] Docs (`Birko.Messaging/CLAUDE.md`, `README.md`, `docs/`) say what `Timeout` bounds and how a
      caller decides whether to retry. The Recent Updates entry names Symbio as able to drop its
      linked-CTS and inner-exception workarounds.

## Out of scope

- A real SMTP server for end-to-end tests. That is Symbio TASK-630, on the consumer side.
- API-based email providers (STORY-007), and SMS / push senders.
- Symbio removing its workarounds. That happens in Symbio after this lands.

## Human test plan

N/A — covered by automated tests through the `_sendMail` test seam.

## Implementation plan

_Decided by the user 2026-10-21: TASK-675 after TASK-697. Shape as the task suggested._

1. `MessageFailureKind` (None / Transient / Permanent / Unknown) on `MessageResult`; `Failed` takes it, default Unknown.
2. `SmtpEmailSender.SendAsync`: a linked deadline at `Timeout`, awaited with `WaitAsync`, so a send ignoring its token
   still returns; the lock and the message are released when the abandoned send really ends.
3. `SmtpFailureClassifier`: reply code, failed recipients, inner exception.
4. Tests through the seam, classifier tests, and a real `SmtpClient` against an in-process stub server.

## Outcome

Done 2026-10-21.

**Measured with a real `SmtpClient` against an in-process stub server:**
- a server that never answers, `Timeout = 1000`: abandoned at 1 014 ms, a `Transient` failure (the async path used to
  ignore `SmtpClient.Timeout`);
- a refused connection: `SmtpException` with an inner `SocketException` → `Transient`;
- `421` at greeting: `SmtpException(ServiceNotAvailable)` → `Transient`;
- `550` to RCPT: `SmtpFailedRecipientException(MailboxUnavailable)` → `Permanent`.

**Decisions:** several refused recipients are `Transient` if any refusal was temporary (a retry resends to all), else
`Permanent`. Failures the sender finds itself (no recipients, no sender, a malformed address, a null batch element)
are `Permanent`. Anything it cannot tell is `Unknown`. The deadline covers the wait for another send too, so a queue
behind a hung send fails fast rather than stalling. An abandoned send holds the client until it ends.

**Tests:** `Task675SmtpTimeoutAndClassificationTests` (19: deadline, caller's cancellation, no deadline, client release,
batch, result kinds, classifier, four real-client cases).

**Mutations, all red:** deadline not kept (2 red); send awaited without the deadline (hangs); timeout reported Unknown (4); caller's cancellation not rethrown (1); lock released at once (2); reply codes ignored (10); inner exception ignored (2); several recipients, any permanent wins (1).

**Symbio** can drop its linked-CTS deadline and its inner-exception reclassification (Symbio § TASK-445, § TASK-561)
once its aggregator picks this up; that is Symbio's to do.
