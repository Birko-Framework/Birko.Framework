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
- **Templates/StringTemplateEngineTests.cs** — Tests StringTemplateEngine simple/multi/nested placeholder replacement, missing property errors, null template/model guards, empty template handling, null property values, and IMessageTemplate rendering.

## Dependencies
- Birko.Messaging (shared project import — core types, email, templates)
- Birko.Time (shared project import)
- Birko.Data.Core (shared project import)
- Birko.Data.Stores (shared project import)
- xUnit (2.9.3)
- FluentAssertions (7.0.0)
- Microsoft.NET.Test.Sdk (18.0.1)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
