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
| `MessageResult` | Send result (Succeeded/Failed with MessageId) |

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

## Templates

```csharp
var engine = new StringTemplateEngine();
var body = await engine.RenderAsync(
    "Hello {{Name}}, your order {{Order.Id}} is ready!",
    new { Name = "John", Order = new { Id = "ORD-123" } });
// Result: "Hello John, your order ORD-123 is ready!"
```

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
