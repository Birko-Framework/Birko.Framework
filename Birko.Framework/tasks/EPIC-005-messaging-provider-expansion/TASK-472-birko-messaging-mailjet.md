---
id: TASK-472
parent: EPIC-005
feature: FEATURE-005
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-19
depends-on: [TASK-471]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Messaging.Mailjet

## Context

Mailjet transactional provider. **Dual-channel**, like [[TASK-471]]: one HTTP API serves transactional
email (Send API v3.1, basic-auth with an API key/secret **pair**) and SMS (a separate bearer token), so a
single `Birko.Messaging.Mailjet` project implements **`IEmailSender` and `ISmsSender`**. That is why it
sits epic-direct rather than under [[STORY-007]] / [[STORY-008]] — the epic splits by channel and this
provider spans both.

State of `Birko.Messaging` today, and why this is not a copy of an existing provider: `SmtpEmailSender` is
the framework's **only** sender implementation, and `Sms/ISmsSender.cs` is an empty marker interface with
**no implementation anywhere** — so no HTTP-API sender and no SMS sender exists to pattern-match against.
Senders return `MessageResult` and never throw for delivery failures (`Birko.Messaging/CLAUDE.md`
§ Key Patterns); bodies render through `ITemplateEngine` / `Birko.Messaging.Razor`.

**`depends-on: TASK-471` is about one shared decision, not about code reuse.** `EmailSettings` extends
`RemoteSettings` and is SMTP-shaped (`Location` = host, `Port`, `UserName`, `Password`, `UseSecure`), which
does not describe an API-key provider. TASK-471 settles whether the descendant reuses `RemoteSettings` or
whether an API-key-shaped base belongs in `Birko.Messaging`; this task **follows that answer** rather than
inventing a second one. Mailjet stresses it harder than Brevo does: email auth is a key **plus** secret and
SMS auth is a **third** credential, so whatever shape TASK-471 picks has to hold three values here.

## Acceptance criteria

- [ ] `Birko.Messaging.Mailjet` shared project exists (`.shproj` + `.projitems`, unique hex GUIDs)
- [ ] Settings descendant following the shape TASK-471 settled, carrying the email API key + secret **and** the SMS token
- [ ] `MailjetEmailSender` implements `IEmailSender` — HTML + plain-text bodies, attachments (including inline/`ContentId`), custom headers, `SendBatchAsync` mapped onto Send API v3.1's native multi-message payload
- [ ] `MailjetSmsSender` implements `ISmsSender` — configured sender name, unicode bodies
- [ ] **Sandbox mode exposed as a settings flag** (v3.1 `SandboxMode` validates without delivering) — a real integration switch, and what makes the human test plan below cheap to re-run
- [ ] Both return `MessageResult` and never throw for delivery failures; transport/auth failures map to the `MessagingException` hierarchy
- [ ] Per-message errors in a partially-successful v3.1 batch surface as individual failed `MessageResult`s, not as one blanket failure
- [ ] Templated bodies work through the existing `ITemplateEngine` / Razor pipeline (no Mailjet-side templates — see Out of scope)
- [ ] If an SDK package is taken rather than raw `HttpClient`: declared in `.projitems` in the **dual CPM-compatible form** and added to `Birko.Packages.props` in the same change
- [ ] Health check added per `CLAUDE-maintenance.md` § Health Check Requirements, consistent with wherever TASK-471 put Brevo's, and `docs/health.md` wired
- [ ] `tests/Birko.Messaging.Mailjet.Tests` — xUnit + FluentAssertions over a mocked `HttpMessageHandler`; success, partial-batch-failure, auth-failure, and cancellation cases on **both** channels
- [ ] Registered in all four build files: `Birko.Framework.slnx` (Messaging/ group), `Birko.Framework.code-workspace`, the test project, and `Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`
- [ ] Indexed in all three docs: `README.md` Projects table, `CLAUDE-projects.md` bullet, `docs/messaging.md`
- [ ] `README.md` + `CLAUDE.md` in the project directory (**no per-project `License.md` / `.gitignore`**)
- [ ] Compiles clean under `-warnaserror` with no CS8600–CS8625

## Out of scope

- **Delivery-status webhooks (bounce / spam / open / click / SMS DLR)** — same boundary as [[TASK-471]]:
  story-level behaviour in [[STORY-007]] that no provider implements and `SmsMessage` has no surface for.
  Needs its own task when the framework-wide mechanism is designed.
- Mailjet-hosted templates and `TemplateID` sends.
- Contacts, lists, segmentation, campaigns.
- Parse API (inbound email) and the Event API receiver.

## Human test plan

- [ ] With `SandboxMode` on and a real key/secret, send an email — Mailjet validates and returns success with **no delivery**; `MessageResult.Succeeded` is true
- [ ] Turn sandbox off and send one HTML email with an attachment to a live inbox — it arrives, HTML renders, attachment opens, and the result carries Mailjet's message ID
- [ ] Send a two-message batch where one recipient address is malformed — the good one is delivered and the results list marks **only** the bad one failed
- [ ] Send one SMS to a real handset, body containing diacritics (`ľščťžýáíé`) — it arrives unmangled with the configured sender name
- [ ] Corrupt the API secret and repeat email + SMS — each returns a failed `MessageResult` with the provider's error text, and **neither throws**

## Implementation plan

_Populated by `/tasks plan TASK-472` — leave empty until then._
