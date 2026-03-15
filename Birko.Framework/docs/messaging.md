# Messaging Guide

## Overview

Birko.Messaging provides unified interfaces for sending email, SMS, and push notifications, with a built-in SMTP email sender and string template engine.

## Core Interfaces

### IMessage

Base interface for all message types:

```csharp
public interface IMessage
{
    string? Id { get; }
    IReadOnlyList<MessageAddress> Recipients { get; }
    string Body { get; }
    DateTimeOffset? ScheduledAt { get; }
    IDictionary<string, string> Metadata { get; }
}
```

### IMessageSender\<T\>

Generic sender with batch support:

```csharp
public interface IMessageSender<in TMessage> where TMessage : IMessage
{
    Task<MessageResult> SendAsync(TMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResult>> SendBatchAsync(
        IEnumerable<TMessage> messages, CancellationToken ct = default);
}
```

### MessageResult

Static factory pattern — never throws for delivery failures:

```csharp
var success = MessageResult.Succeeded("msg-123");
var failure = MessageResult.Failed("Connection refused", exception);

if (result.Success)
    Console.WriteLine($"Sent: {result.MessageId}");
else
    Console.WriteLine($"Error: {result.Error}");
```

### MessageAddress

Represents a recipient (email, phone number, or device token):

```csharp
var email = new MessageAddress("user@example.com", "John Doe");
var phone = new MessageAddress("+1234567890");

// Case-insensitive equality on Value
email.Equals(new MessageAddress("USER@EXAMPLE.COM")); // true

// Formatted output
email.ToString(); // "John Doe <user@example.com>"
```

## Email

### EmailSettings

Extends `RemoteSettings` from Birko.Data.Stores (inherits Host/Port/UserName/Password/UseSecure):

```csharp
var settings = new EmailSettings("smtp.example.com", 587, "user", "pass")
{
    UseSecure = true,      // inherited from RemoteSettings, defaults to true
    Timeout = 30000,       // ms, default 30s
    DefaultFrom = new MessageAddress("noreply@example.com", "My App")
};
```

### SmtpEmailSender

Built-in SMTP implementation using `System.Net.Mail`:

```csharp
using var sender = new SmtpEmailSender(settings);

// Simple convenience method
var result = await sender.SendAsync(
    new MessageAddress("from@example.com"),
    new MessageAddress("to@example.com"),
    "Subject",
    "<p>HTML body</p>",
    isHtml: true);

// Full message with all options
var email = new EmailMessage
{
    From = new MessageAddress("noreply@example.com", "App"),
    Recipients = new[] { new MessageAddress("user@example.com") },
    Cc = new[] { new MessageAddress("manager@example.com") },
    Bcc = new[] { new MessageAddress("archive@example.com") },
    ReplyTo = new MessageAddress("support@example.com"),
    Subject = "Invoice #123",
    Body = "<p>Your invoice is attached.</p>",
    IsHtml = true,
    PlainTextBody = "Your invoice is attached.",  // alternative plain text
    Priority = MessagePriority.High,
    Attachments = new[] { new MessageAttachment("invoice.pdf", "application/pdf", stream) },
    Headers = { ["X-Custom-Header"] = "value" }
};
var result = await sender.SendAsync(email);

// Batch sending
var results = await sender.SendBatchAsync(new[] { email1, email2, email3 });
```

### Attachments

```csharp
// Regular attachment
var attachment = new MessageAttachment("report.pdf", "application/pdf", fileStream);

// Inline image (for HTML body)
var inline = new MessageAttachment("logo.png", "image/png", logoStream,
    isInline: true, contentId: "logo-cid");
// Reference in HTML: <img src="cid:logo-cid" />
```

## Templates

### ITemplateEngine

```csharp
public interface ITemplateEngine
{
    Task<string> RenderAsync(string template, object model, CancellationToken ct = default);
    Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default);
}
```

### StringTemplateEngine

Built-in `{{placeholder}}` replacement with nested property support:

```csharp
var engine = new StringTemplateEngine();

// Simple placeholders
var body = await engine.RenderAsync(
    "Hello {{Name}}, your order {{OrderId}} is ready!",
    new { Name = "John", OrderId = "ORD-123" });
// "Hello John, your order ORD-123 is ready!"

// Nested properties
var body = await engine.RenderAsync(
    "Dear {{Customer.Name}}, your delivery to {{Customer.Address.City}} is on the way.",
    new { Customer = new { Name = "Alice", Address = new { City = "Prague" } } });
// "Dear Alice, your delivery to Prague is on the way."
```

### IMessageTemplate

Define reusable templates:

```csharp
public class InvoiceTemplate : IMessageTemplate
{
    public string Name => "invoice";
    public string Subject => "Invoice #{{InvoiceNumber}}";
    public string BodyTemplate => "<p>Dear {{CustomerName}}, your invoice for {{Amount}} is attached.</p>";
    public bool IsHtml => true;
}

var template = new InvoiceTemplate();
var body = await engine.RenderAsync(template,
    new { InvoiceNumber = "INV-001", CustomerName = "Alice", Amount = "$99.00" });
```

## SMS (Interface Only)

```csharp
var sms = new SmsMessage
{
    From = new MessageAddress("+1234567890"),
    Recipients = new[] { new MessageAddress("+0987654321") },
    Body = "Your verification code is 123456"
};
// Send via ISmsSender implementation (e.g., Birko.Messaging.Twilio)
```

## Push Notifications (Interface Only)

```csharp
var push = new PushMessage
{
    Recipients = new[] { new MessageAddress("device-token-abc") },
    Title = "New Message",
    Body = "You have a new notification",
    Badge = 1,
    Sound = "default",
    Data = { ["orderId"] = "ORD-123" },
    ClickAction = "OPEN_ORDER"
};
// Send via IPushSender implementation (e.g., Birko.Messaging.Firebase)
```

## Error Handling

Senders return `MessageResult.Failed()` for delivery errors — they don't throw:

```csharp
var result = await sender.SendAsync(email);
if (!result.Success)
{
    logger.LogError("Email failed: {Error}", result.Error);
    // result.Exception contains the original exception if available
}
```

Exception types for programming errors (null args, invalid templates):

| Exception | When |
|-----------|------|
| `MessagingException` | Base exception |
| `MessageDeliveryException` | Delivery failure (includes MessageId) |
| `InvalidRecipientException` | Invalid recipient address |
| `TemplateRenderException` | Template rendering failure (missing property, etc.) |

## Provider Projects (Planned)

| Project | Channel | Backend |
|---------|---------|---------|
| `Birko.Messaging.SendGrid` | Email | SendGrid API |
| `Birko.Messaging.Mailgun` | Email | Mailgun API |
| `Birko.Messaging.Twilio` | SMS | Twilio API |
| `Birko.Messaging.Firebase` | Push | Firebase Cloud Messaging |
| `Birko.Messaging.Apple` | Push | Apple Push Notification Service |

## See Also

- [Birko.Messaging CLAUDE.md](../../Birko.Messaging/CLAUDE.md)
