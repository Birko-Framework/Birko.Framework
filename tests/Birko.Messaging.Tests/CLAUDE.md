# Birko.Messaging.Tests

## Overview
Test suite for the Birko.Messaging library. Validates core message types (address, attachment, result), email messaging (settings, message construction, SMTP sender), and the string template engine using xUnit and FluentAssertions.

## Project Location
`tests/Birko.Messaging.Tests/` — Test project (.csproj)

## Components
- **Core/MessageAddressTests.cs** — Tests MessageAddress construction, ToString formatting, case-insensitive equality, and GetHashCode consistency.
- **Core/MessageAttachmentTests.cs** — Tests MessageAttachment construction with required/optional parameters, null guard validation, and default IsInline value.
- **Core/MessageResultTests.cs** — Tests MessageResult.Succeeded/Failed factory methods, MessageId handling, Exception attachment, and Timestamp accuracy.
- **Email/EmailMessageTests.cs** — Tests EmailMessage default values and full property round-trip (From, Recipients, Cc, Bcc, ReplyTo, Headers, Metadata, Priority).
- **Email/EmailSettingsTests.cs** — Tests EmailSettings constructors, default values (UseSecure, Timeout), and LoadFrom property copying.
- **Email/SmtpEmailSenderTests.cs** — Tests SmtpEmailSender constructor validation, null/empty recipient handling, convenience SendAsync overload, SendBatchAsync edge cases, and Dispose behavior. Uses localhost SMTP (no real server required).
- **Email/SmtpEmailSenderWireTests.cs** — TASK-585: the MIME on the wire. Sends through a real SmtpClient into a pickup directory and parses the `.eml` (part types, order, charsets, decoded text, `multipart/related` for inline images). Asserts on the wire, never on `MailMessage` properties.
- **Templates/StringTemplateEngineTests.cs** — Tests StringTemplateEngine simple/multi/nested placeholder replacement, missing property errors, null template/model guards, empty template handling, null property values, and IMessageTemplate rendering.
- **Templates/TemplateValueEncodingTests.cs** — TASK-586: per-call encoding with caller-declared raw paths; no encoder is byte-identical to before; every printable ASCII character renders as itself through Markdig; in Symbio's Markdig pipeline escaped customer values render as text while a raw tracking link still autolinks, with the unencoded template as the control; engines without encoding refuse an encoder. Uses Markdig 1.4.0, test-only.

## Dependencies
- Birko.Messaging (shared project import — core types, email, templates)
- Birko.Time (shared project import)
- Birko.Data.Core (shared project import)
- Birko.Data.Stores (shared project import)
- Markdig (1.4.0, test-only: renders encoded template values)
- xUnit (2.9.3)
- FluentAssertions (7.0.0)
- Microsoft.NET.Test.Sdk (18.0.1)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
