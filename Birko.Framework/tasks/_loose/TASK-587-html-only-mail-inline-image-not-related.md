---
id: TASK-587
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
findings: [FIELD-032]
pr: null
github-issue: null
jira-key: null
---

# An HTML-only mail puts its inline image beside the HTML, where `cid:` has no defined resolution

## Context

Found while doing TASK-585 (its close-gate review). `SmtpEmailSender.BuildMailMessage`
(`Birko.Messaging/Email/SmtpEmailSender.cs`) now makes an inline attachment with a `ContentId` a `LinkedResource` of the
HTML view, but **only when `PlainTextBody` is also set**, because only then is there an HTML view. With an HTML body
alone, the image is still added to `mail.Attachments`, so the mail is `multipart/mixed{ text/html, image }`. RFC 2387
defines `cid:` resolution within `multipart/related`. Clients that resolve it in `mixed` do so by leniency, and others
show the image as an attachment and a broken image in the body. TASK-585 left this path unchanged on purpose: its
criterion 2 is "one body, unchanged".

## Acceptance criteria

- [ ] An HTML-only mail with an inline `ContentId` attachment is `multipart/related{ text/html, image }` on the wire (one
      HTML `AlternateView` carrying the image as a `LinkedResource`, no `Body`), and a non-inline attachment still
      makes the whole `multipart/mixed`.
- [ ] An HTML-only mail **without** inline images stays a single `text/html` part.
- [ ] Asserted in `SmtpEmailSenderWireTests` (pickup-directory `.eml`), and the old construction turns it red.

## Out of scope

- The both-bodies case: done in TASK-585.

## Human test plan

N/A — the MIME structure is asserted on the wire; TASK-585's human test (a real client) covers rendering.
