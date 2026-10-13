---
id: TASK-585
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SmtpEmailSender sends the HTML part labelled text/plain, and first, when `PlainTextBody` is set

## Context

Found by Symbio TASK-917 (2026-10-13) on the wire, with a local SMTP catcher. `SmtpEmailSender.BuildMailMessage` sets
`Body` + `IsBodyHtml = true` and adds `PlainTextBody` as an `AlternateView` (`Email/SmtpEmailSender.cs:188`). Once any
`AlternateView` exists, `System.Net.Mail` **ignores `IsBodyHtml`**: the body is emitted as `text/plain`. It is also
emitted **first**, while RFC 2046 makes the **last** alternative the preferred one. The result is a
`multipart/alternative` mail with two `text/plain` parts, so no client renders the HTML.

Reproduced in isolation (pickup directory, no Symbio):

| construction | parts on the wire |
|---|---|
| `Body` = html, `IsBodyHtml = true`, plain added as `AlternateView` (today) | `text/plain; charset=us-ascii`, `text/plain; charset=us-ascii` |
| no `Body`; `AlternateViews` = plain (UTF-8), then html (UTF-8) | `text/plain; charset=utf-8`, `text/html; charset=utf-8` |

The charset is also lost (`us-ascii`): the plain view is created with a `null` encoding.

Symbio works around it by **not** setting `PlainTextBody`, so its mail stays a single, correctly labelled `text/html` part.
It sets the property again once this lands.

## Acceptance criteria

- [ ] With `IsHtml` and a `PlainTextBody`, the mail is `multipart/alternative`: `text/plain` first, `text/html` last,
      both `charset=utf-8`.
- [ ] Without `PlainTextBody`, the mail is unchanged: one part, `text/html` or `text/plain` per `IsHtml`.
- [ ] Inline attachments (`ContentId`) still resolve from the HTML part. Today they are `Attachments`, so check whether
      an HTML view needs them as `LinkedResources` instead.
- [ ] A test asserts the MIME on the wire: content types and order from a pickup-directory send. It must not assert on
      `MailMessage` properties, which is exactly where this bug hides.

## Out of scope

- Generating the plain-text part from HTML. The caller supplies it.

## Human test plan

Send with both bodies to a local SMTP catcher, then open the mail in a real client. Expected: the HTML renders, and
the source view shows a text/plain part followed by a text/html part.

## Implementation plan

1. In `BuildMailMessage`, when `IsHtml` and `PlainTextBody` are both set, leave `Body` empty. Add an `AlternateView`
   for the plain text, then one for the HTML, each with `Encoding.UTF8` and an explicit media type.
2. Wire test via `SmtpDeliveryMethod.SpecifiedPickupDirectory`.

## Progress log
