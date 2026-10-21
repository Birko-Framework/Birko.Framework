# Birko.Messaging

Core messaging framework for the Birko Framework. Provides unified interfaces for sending email, SMS, and push notifications, plus a built-in SMTP email sender and template engine.

## Features

- **Unified messaging interface** - `IMessageSender<T>` for type-safe sending across all channels
- **Email** - `IEmailSender` with built-in `SmtpEmailSender` (System.Net.Mail)
- **SMS** - `ISmsSender` interface for provider implementations (Twilio, etc.)
- **Push Notifications** - `IPushSender` interface for provider implementations (FCM, APNs)
- **Template Engine** - `ITemplateEngine` with built-in `StringTemplateEngine` (`{{placeholder}}` syntax)
- **Batch sending** - `SendBatchAsync` for bulk operations
- **Attachments** - File attachments and inline images for email
- **Message scheduling** - `ScheduledAt` for deferred delivery
- **Custom metadata** - Provider-specific extensions via `Metadata` dictionary

## Core Types

| Type | Description |
|------|-------------|
| `IMessage` | Base interface for all message types |
| `IMessageSender<T>` | Generic sender interface (Send + SendBatch) |
| `IMessageTemplate` | Template with Name, Subject, BodyTemplate |
| `MessageAddress` | Recipient address (Value + DisplayName) |
| `MessageAttachment` | File attachment (FileName, ContentType, Stream) |
| `MessageResult` | Send result (Succeeded/Failed with MessageId); `FailureKind` says whether a failure is worth retrying |
| `MessageFailureKind` | None / Transient / Permanent / Unknown |

## Email

```csharp
// UseSecure defaults to true (inherited from RemoteSettings)
var settings = new EmailSettings("smtp.example.com", 587, "user", "pass");
using var sender = new SmtpEmailSender(settings);

// Simple send
var result = await sender.SendAsync(
    new MessageAddress("sender@example.com", "Sender"),
    new MessageAddress("recipient@example.com"),
    "Subject",
    "<p>HTML body</p>",
    isHtml: true);

// Full message
var email = new EmailMessage
{
    From = new MessageAddress("noreply@example.com", "App"),
    Recipients = new[] { new MessageAddress("user@example.com") },
    Subject = "Invoice #123",
    Body = "<p>Your invoice is attached.</p>",
    IsHtml = true,
    PlainTextBody = "Your invoice is attached.",
    Priority = MessagePriority.High,
    Attachments = new[] { new MessageAttachment("invoice.pdf", "application/pdf", stream) }
};
var result = await sender.SendAsync(email);
```

With both `Body` (HTML) and `PlainTextBody`, the mail is `multipart/alternative`: a UTF-8 text/plain part, then the
UTF-8 text/html part that clients prefer. An inline attachment (`isInline: true` with a `contentId`) is related to
the HTML part, so `<img src="cid:...">` resolves.

## Templates

```csharp
var engine = new StringTemplateEngine();
var body = await engine.RenderAsync(
    "Hello {{Name}}, your order {{Order.Id}} is ready!",
    new { Name = "John", Order = new { Id = "ORD-123" } });
// Result: "Hello John, your order ORD-123 is ready!"
```

Optional sections render only when the value is present — not `null`, not blank, not `false` (`0` is present):

```csharp
await engine.RenderAsync("Order {{Number}}.{{#if TrackingUrl}} Track it: {{TrackingUrl}}{{/if}}", model);
```

Sections do not nest and have no else branch. An unbalanced, nested or unknown `{{#…}}` / `{{/…}}` marker throws
`TemplateRenderException` instead of reaching the recipient.

**Encoding merged values.** When the rendered text is a markup format, pass an encoder for the call, so merged data
cannot become markup. Every value is encoded except the paths the calling code declares raw:

```csharp
var markdown = new TemplateRenderOptions
{
    Encoder = new MarkdownValueEncoder(),                 // backslash-escapes ASCII punctuation
    RawPaths = new HashSet<string> { "TrackingUrl" },     // a link the system built: merged as is, so it autolinks
};
var subject = await engine.RenderAsync(template.Subject, model, ct);            // plain text: no encoder
var body    = await engine.RenderAsync(template.BodyTemplate, model, markdown, ct);
```

- Without an encoder the output is exactly what the overload without options gives.
- `RawPaths` are exact, case-sensitive paths; a misspelt one fails safe (the value is encoded and shows as text).
- `{{#if}}` sections are decided on the raw value.
- `MarkdownValueEncoder` also drops a value's leading indentation and blank lines (they would start a code block
  or end the paragraph); its line breaks are kept.
- The encoded text carries backslashes. Do not send it as a plain-text alternative; render the template again
  without the encoder for that.
- An engine that cannot encode (`RazorTemplateEngine`, which HTML-encodes on its own) throws `NotSupportedException`
  when given an encoder, rather than merging the values unencoded.

## SMS / Push (Interface Only)

SMS and push notification sender interfaces are defined in core. Provider implementations are in separate projects:

- `Birko.Messaging.Twilio` - Twilio SMS (planned)
- `Birko.Messaging.SendGrid` - SendGrid email (planned)
- `Birko.Messaging.Firebase` - Firebase Cloud Messaging (planned)
- `Birko.Messaging.Apple` - Apple Push Notification Service (planned)

## Dependencies

- `Birko.Data.Core` - ILoadable interface
- `Birko.Data.Stores` - RemoteSettings base class
- `System.Net.Mail` - Built-in SMTP (no external NuGet)

## License

MIT License - see [License.md](License.md)
