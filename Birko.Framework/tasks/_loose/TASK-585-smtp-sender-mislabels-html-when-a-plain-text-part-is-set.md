---
id: TASK-585
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
findings: [FIELD-031]
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

- [x] With `IsHtml` and a `PlainTextBody`, the mail is `multipart/alternative`: `text/plain` first, `text/html` last,
      both `charset=utf-8`.
- [x] Without `PlainTextBody`, the mail is unchanged: one part, `text/html` or `text/plain` per `IsHtml`.
- [x] Inline attachments (`ContentId`) still resolve from the HTML part. Today they are `Attachments`, so check whether
      an HTML view needs them as `LinkedResources` instead.
- [x] A test asserts the MIME on the wire: content types and order from a pickup-directory send. It must not assert on
      `MailMessage` properties, which is exactly where this bug hides.

## Out of scope

- Generating the plain-text part from HTML. The caller supplies it.
- An HTML-only mail's inline image (still `multipart/mixed`): TASK-587.

## Human test plan

- [x] Send with both bodies to a local SMTP catcher, then open the mail in a real client. Expected: the HTML renders,
      and the source view shows a text/plain part followed by a text/html part.
  - **Passed 2026-10-13:** sent over real SMTP through the public `SmtpEmailSender` constructor to Mailpit v1.31.5
    (Docker, `localhost:1025`), with both bodies and an inline PNG. The user checked the Mailpit UI: the HTML tab
    renders the heading, the Slovak diacritics and the inline image in the body; the Text tab shows the plain part; the
    raw source has text/plain before text/html. Mailpit's own parse: HTML and text parts present, the image inline
    (CID `logo`), 0 attachments.

## Implementation plan

Refined at pick, 2026-10-13. Code: `Birko.Messaging/Email/SmtpEmailSender.cs` `BuildMailMessage`. Tests:
`tests/Birko.Messaging.Tests/Email/SmtpEmailSenderWireTests.cs` (new; it carries a small MIME reader).

1. **Both bodies** (`IsHtml` and a non-empty `PlainTextBody`): leave `Body` empty, then add two `AlternateView`s,
   plain first and HTML last, each `Encoding.UTF8` with an explicit media type (`text/plain`, `text/html`).
2. **One body:** unchanged (`Body` + `IsBodyHtml`), so a single `text/html` or `text/plain` part. Set
   `BodyEncoding = UTF8` only if the wire shows the single part losing non-ASCII today; measure, don't assume.
3. **Inline attachments (decision, to be confirmed on the wire):** in the both-bodies case, an inline attachment with
   a `ContentId` becomes a `LinkedResource` on the HTML view, so the HTML part is `multipart/related{text/html, image}`
   and `cid:` resolves by the RFC 2387 rule. As a sibling `Attachment` it would sit in `multipart/mixed` next to the
   alternative, where `cid:` resolution is undefined. Non-inline attachments stay `Attachments`, and the one-body case
   keeps today's structure (criterion 2).
4. **Wire tests** through the existing internal constructor: the send delegate hands the `MailMessage` to an
   `SmtpClient` with `SpecifiedPickupDirectory`, and the test parses the `.eml`'s MIME headers, never `MailMessage`
   properties. Cases: both bodies (types, order, charsets), HTML only, plain only, both bodies with an inline image
   (the related part holds the HTML and the `Content-ID`), and non-ASCII text surviving.
5. **Mutation check:** put the old construction back, and the both-bodies test goes red.
6. Docs: `Birko.Messaging` CLAUDE.md/README if they describe the MIME shape; a CHANGELOG/Recent Updates note per the
   framework's rules (behaviour on the wire changes for callers who set `PlainTextBody`).

## Progress log

- 2026-10-13 — Fixed in `Birko.Messaging/Email/SmtpEmailSender.cs`; wire tests in `SmtpEmailSenderWireTests` (5 cases).
  - Red first, against today's code: 3 of 5 failed (`{"text/plain", "text/plain"}`), the two single-body cases passed,
    non-ASCII included, so plan step 2 needed no encoding change.
  - With the fix, Messaging tests 69/69. Mutation checks: the old construction fails the 3 both-bodies tests; sending
    inline images as plain `Attachment`s again fails only the inline test.
  - **Wire output, both bodies:** `multipart/alternative{ text/plain; charset=utf-8, text/html; charset=utf-8 }`, both
    base64. **With an inline image:** `alternative{ text/plain, multipart/related; type="text/html"{ text/html,
    image/png Content-ID <logo> } }`.
  - Close gate, independent reviewer: no blockers; stream ownership unchanged (the `MailMessage` disposes views and
    linked resources as it disposed attachments). Applied: temp-dir cleanup also tolerates `UnauthorizedAccessException`,
    the test's decision comment trimmed. Spawned TASK-587 (the HTML-only inline image, left unchanged here by criterion 2).
  - Docs: Messaging CLAUDE.md/README, the tests' CLAUDE.md/README, framework Recent Updates.
  - Status `verify`: the human test plan (a real mail client) has not been run. Symbio re-enabling `PlainTextBody` is
    a separate Symbio change.
- 2026-10-13 — Human test passed on Mailpit (real SMTP, see the test plan). Done. Symbio re-enabling `PlainTextBody`
  (removing its TASK-585 workaround) is a Symbio change.
