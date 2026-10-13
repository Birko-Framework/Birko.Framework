# Birko.Messaging

## Overview
Core messaging framework providing unified interfaces for email, SMS, and push notifications, plus a built-in SMTP email sender and string template engine.

## Project Location
`Birko.Messaging/` (shared project: `.shproj` + `.projitems`)

## Components

### Core (`Birko.Messaging`)
- **IMessage** - Base interface (Id, Recipients, Body, ScheduledAt, Metadata)
- **IMessageSender\<T\>** - Generic sender (SendAsync, SendBatchAsync)
- **IMessageTemplate** - Template definition (Name, Subject, BodyTemplate, IsHtml)
- **MessageAddress** - Recipient address (Value + DisplayName), case-insensitive equality
- **MessageAttachment** - File attachment (FileName, ContentType, Stream, IsInline, ContentId)
- **MessageResult** - Result with static factories (Succeeded/Failed), includes MessageId and Timestamp
- **MessagingException** - Base exception hierarchy (MessageDeliveryException, InvalidRecipientException, TemplateRenderException)

### Email (`Birko.Messaging.Email`)
- **EmailMessage** - IMessage with From, To, Cc, Bcc, ReplyTo, Subject, IsHtml, PlainTextBody, Attachments, Priority, Headers
- **EmailSettings** - Extends RemoteSettings (Host/Port/UserName/Password + UseSecure, Timeout, DefaultFrom)
- **IEmailSender** - Extends IMessageSender\<EmailMessage\> with convenience overload
- **SmtpEmailSender** - Built-in SMTP implementation using System.Net.Mail, IDisposable

### SMS (`Birko.Messaging.Sms`)
- **SmsMessage** - IMessage with From (phone number)
- **ISmsSender** - Interface only, implementations in provider projects

### Push (`Birko.Messaging.Push`)
- **PushMessage** - IMessage with Title, ImageUrl, ClickAction, Data, Badge, Sound
- **IPushSender** - Interface only, implementations in provider projects

### Templates (`Birko.Messaging.Templates`)
- **ITemplateEngine** - RenderAsync(template, model) and RenderAsync(IMessageTemplate, model); TASK-586 overloads take `TemplateRenderOptions` (per-call `ITemplateValueEncoder` + caller-declared `RawPaths`). They are default interface methods that refuse an encoder, so an engine that cannot encode never merges unencoded values silently
- **TemplateRenderOptions / ITemplateValueEncoder / MarkdownValueEncoder** - per-call value encoding; with an encoder set every value is encoded except the exact `RawPaths` (fails closed). Chosen per call, not per placeholder or attribute: one model renders a plain subject and a Markdown body, and templates are tenant-authored (decision in TASK-586)
- **StringTemplateEngine** - `{{Property.SubProperty}}` replacement via reflection, GeneratedRegex; `{{#if Path}}…{{/if}}` optional sections (present = not null / blank / `false`; no nesting, no else; any leftover `{{#`/`{{/` marker throws)

## Dependencies
- **Birko.Data.Core** - ILoadable\<T\> interface
- **Birko.Data.Stores** - RemoteSettings base class (for EmailSettings)
- **System.Net.Mail** - Built-in .NET SMTP

## Key Patterns
- MessageResult uses static factory pattern (Succeeded/Failed), not constructors
- EmailSettings extends RemoteSettings for host/port/credentials consistency
- SmtpEmailSender is IDisposable (wraps SmtpClient)
- SmtpEmailSender MIME shape (TASK-585): `IsHtml` + `PlainTextBody` → `multipart/alternative`, text/plain first and text/html last, both UTF-8; an inline attachment with a `ContentId` is a `LinkedResource` of the HTML view (`multipart/related`). One body → one part. Never mix `Body`/`IsBodyHtml` with an `AlternateView`: System.Net.Mail then sends the body as text/plain. `SmtpEmailSenderWireTests` asserts the `.eml`, not `MailMessage`
- StringTemplateEngine uses GeneratedRegex for performance
- All senders return MessageResult (never throw for delivery failures)

## Maintenance
- When adding new message types, implement IMessage
- When adding new providers, implement IMessageSender\<T\> or the channel-specific interface
- When adding new template engines, implement ITemplateEngine
