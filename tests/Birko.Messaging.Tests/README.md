# Birko.Messaging.Tests

Unit tests for the Birko.Messaging library covering core message types, email sending, and template rendering.

## Project Location
`C:\Source\Birko.Messaging.Tests\` — test project (.csproj, net10.0)

## Components

- **Core/MessageAddressTests.cs** — Tests for `MessageAddress` construction, display name formatting, case-insensitive equality, and null-argument guards.
- **Core/MessageAttachmentTests.cs** — Tests for `MessageAttachment` construction, stream/content validation, and default inline behavior.
- **Core/MessageResultTests.cs** — Tests for `MessageResult.Succeeded()` / `.Failed()` factory methods, error/exception capture, and timestamp accuracy.
- **Email/EmailMessageTests.cs** — Tests for `EmailMessage` default values and full property round-tripping (recipients, CC, BCC, headers, metadata, priority).
- **Email/EmailSettingsTests.cs** — Tests for `EmailSettings` constructor, credential handling, secure/timeout defaults, and `LoadFrom` copy behavior.
- **Email/SmtpEmailSenderTests.cs** — Tests for `SmtpEmailSender` argument validation, empty-recipient and missing-sender guard logic, batch operations, and disposal.
- **Templates/StringTemplateEngineTests.cs** — Tests for `StringTemplateEngine` placeholder replacement, nested property resolution, null handling, and `IMessageTemplate` rendering.

## Dependencies

- Birko.Messaging (shared project import)
- Birko.Time (shared project import)
- Birko.Data.Core (shared project import)
- Birko.Data.Stores (shared project import)
- xUnit (2.9.3)
- FluentAssertions (7.0.0)
- Microsoft.NET.Test.Sdk (18.0.1)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
