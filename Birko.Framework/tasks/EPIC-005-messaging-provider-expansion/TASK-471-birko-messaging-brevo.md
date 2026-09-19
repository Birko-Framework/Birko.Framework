---
id: TASK-471
parent: EPIC-005
feature: FEATURE-005
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-19
depends-on: []
blocks: [TASK-472]
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Messaging.Brevo

## Context

Brevo (formerly Sendinblue) transactional provider. **Dual-channel:** one HTTP API and one API key serve
both transactional email and SMS, so a single `Birko.Messaging.Brevo` project implements **`IEmailSender`
and `ISmsSender`** off one settings descendant and one `HttpClient`. That is why this task sits
epic-direct rather than under [[STORY-007]] (email providers) or [[STORY-008]] (SMS via Twilio) — the
epic's decomposition splits by channel and this provider spans both. Splitting it into two projects to
fit the stories would duplicate the client, the settings and the error mapping for no gain.

What exists today in `Birko.Messaging`:

- `Email/IEmailSender.cs` — `IMessageSender<EmailMessage>` plus a `SendAsync(from, to, subject, body, isHtml, ct)` convenience overload.
- `Email/SmtpEmailSender.cs` — **the only sender implementation in the framework**, over `System.Net.Mail`.
- `Sms/ISmsSender.cs` — `public interface ISmsSender : IMessageSender<SmsMessage> { }`, **no implementation
  anywhere**. This task would be the first, so the SMS path has never been exercised against a real provider.
- `Core/MessageResult.cs` — `Succeeded`/`Failed` static factories; senders return it and **never throw for
  delivery failures** (`Birko.Messaging/CLAUDE.md` § Key Patterns).
- `Templates/` + `Birko.Messaging.Razor` — the existing body-rendering pipeline, reusable as-is.

**The settings shape is the one real design question, and it is shared with [[TASK-472]].**
`Email/EmailSettings.cs` extends `RemoteSettings` and is SMTP-shaped: `Location` is a host, plus `Port`,
`UserName`, `Password`, `UseSecure`. An HTTP-API provider has an API base URL and an API key and no port
or username. Decide in the implementation plan whether the descendant maps `Location` → API base URL and
`Password` → API key, or whether an API-key-shaped sibling base belongs in `Birko.Messaging` instead.
**Whichever this task settles, [[TASK-472]] follows** — hence `blocks:`; the point of ordering them is to
avoid two incompatible answers to one question, not to gate the work.

## Acceptance criteria

- [ ] `Birko.Messaging.Brevo` shared project exists (`.shproj` + `.projitems`, unique hex GUIDs)
- [ ] Settings descendant carrying API key + API base URL, with the `RemoteSettings`-vs-new-base decision recorded in the implementation plan
- [ ] `BrevoEmailSender` implements `IEmailSender` — HTML + plain-text bodies, attachments, custom headers, `SendBatchAsync`
- [ ] `BrevoSmsSender` implements `ISmsSender` — sender name/number, unicode bodies, message-count/segment reporting where the API returns it
- [ ] Both return `MessageResult` and never throw for delivery failures; transport/auth failures map to the `MessagingException` hierarchy
- [ ] Templated bodies work through the existing `ITemplateEngine` / `Birko.Messaging.Razor` pipeline (no Brevo-side templates — see Out of scope)
- [ ] One `HttpClient` shared by both senders; `IDisposable` handled the way `SmtpEmailSender` does
- [ ] If an SDK package is taken rather than raw `HttpClient`: declared in `.projitems` in the **dual CPM-compatible form** and added to `Birko.Packages.props` in the same change (`CLAUDE-maintenance.md` § External dependencies)
- [ ] Health check added — Brevo is an external service, so `CLAUDE-maintenance.md` § Health Check Requirements applies; place it in an existing `Birko.Health.*` sibling or a new one, and wire `docs/health.md`
- [ ] `tests/Birko.Messaging.Brevo.Tests` — xUnit + FluentAssertions over a mocked `HttpMessageHandler`; success, provider-error, auth-failure, and cancellation cases on **both** channels
- [ ] Registered in all four build files: `Birko.Framework.slnx` (Messaging/ group), `Birko.Framework.code-workspace`, the test project, and `Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`
- [ ] Indexed in all three docs: `README.md` Projects table, `CLAUDE-projects.md` bullet, `docs/messaging.md`
- [ ] `README.md` + `CLAUDE.md` in the project directory (**no per-project `License.md` / `.gitignore`** — the monorepo has one root each; the sibling tasks [[TASK-009]]/[[TASK-010]]/[[TASK-011]] still ask for them and predate the consolidation)
- [ ] Compiles clean under `-warnaserror` with no CS8600–CS8625

## Out of scope

- **Delivery-status webhooks (bounce / spam / open / click / SMS DLR).** [[STORY-007]] names event-pipeline
  surfacing as story-level behaviour and no provider implements it yet; `SmsMessage` has no callback surface
  at all. Filing a receiver here would build one provider's half of a framework-wide mechanism — it needs its
  own task when the mechanism is designed.
- Brevo-hosted templates and template IDs — Birko renders bodies through `ITemplateEngine`; using the
  provider's template store instead is a different capability.
- Marketing API: contacts, lists, campaigns, automation.
- WhatsApp and Brevo Conversations.
- Inbound email parsing.

## Human test plan

Automated tests mock the transport, so nothing above proves the request shape Brevo actually accepts.

- [ ] With a real Brevo API key, send one HTML email with an attachment to a live inbox — it arrives, the HTML renders, the attachment opens, and `MessageResult.Succeeded` carries the provider's message ID
- [ ] Send one SMS to a real handset — it arrives, the sender name shows as configured, and a body with diacritics (`ľščťžýáíé`) is not mangled
- [ ] Revoke or corrupt the API key and repeat both — each returns a **failed** `MessageResult` with the provider's error text, and **neither throws**
- [ ] Point the settings at an unreachable base URL — same: failed result, no throw, no hang past the configured timeout

## Implementation plan

_Populated by `/tasks plan TASK-471` — leave empty until then._
