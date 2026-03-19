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

Extends `RemoteSettings` from Birko.Settings (namespace `Birko.Configuration`) (inherits Host/Port/UserName/Password/UseSecure):

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

### RazorTemplateEngine (Birko.Messaging.Razor)

Full Razor syntax for complex HTML email templates — conditionals, loops, strongly-typed models, and layouts. Drop-in replacement for `StringTemplateEngine` when `{{placeholder}}` syntax is insufficient.

> **Dependency:** The consuming project must add the [RazorLight](https://github.com/toddams/RazorLight) NuGet package:
> ```xml
> <PackageReference Include="RazorLight" Version="2.*" />
> ```
> And set `<PreserveCompilationContext>true</PreserveCompilationContext>` in the `.csproj`.

#### Inline Razor Templates

```csharp
var engine = new RazorTemplateEngine();

var body = await engine.RenderAsync(
    "Hello @Model.Name, your order @Model.OrderId is ready!",
    new { Name = "John", OrderId = "ORD-123" });
```

#### Razor Conditionals and Loops

```csharp
var body = await engine.RenderAsync(@"
<h1>Order for @Model.CustomerName</h1>
<ul>
@foreach (var item in Model.Items)
{
    <li>@item.Name — @item.Price.ToString(""C"")</li>
}
</ul>
@if (Model.IsVip)
{
    <p><strong>VIP discount applied!</strong></p>
}
<p>Total: @Model.Total.ToString(""C"")</p>",
    new { CustomerName = "Alice", Items = items, IsVip = true, Total = 99.00m });
```

#### File-Based .cshtml Templates

```csharp
var engine = new RazorTemplateEngine(new RazorTemplateOptions
{
    TemplateBasePath = "/app/templates/emails",
    EnableCaching = true,
    DefaultNamespaces = new[] { "System.Linq" }
});

// Renders /app/templates/emails/OrderConfirmation.cshtml
var body = await engine.RenderFileAsync("OrderConfirmation", order);

// Subdirectories supported
var welcome = await engine.RenderFileAsync("Emails/Welcome", user);
```

#### With IMessageTemplate

When `TemplateBasePath` is configured, `RenderAsync(IMessageTemplate, ...)` tries to find a file matching the template's `Name` first. If no file exists, it falls back to the inline `BodyTemplate`:

```csharp
var template = new InvoiceTemplate(); // Name = "invoice"
// Tries: /app/templates/emails/invoice.cshtml
// Falls back to: template.BodyTemplate (inline Razor)
var body = await engine.RenderAsync(template, model);
```

#### RazorTemplateOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TemplateBasePath` | `string?` | `null` | Base directory for .cshtml files. `null` = inline-only mode |
| `FileExtension` | `string` | `".cshtml"` | Template file extension |
| `EnableCaching` | `bool` | `true` | Cache compiled templates for reuse |
| `FileEncoding` | `Encoding` | `UTF8` | Encoding for reading template files |
| `DefaultNamespaces` | `string[]` | `[]` | Namespaces auto-imported in all templates |

#### RazorFileTemplateProvider

Handles file loading separately from the engine. Includes:
- In-memory content caching (per template name)
- Directory traversal protection (`../` escape blocked)
- Path normalization (forward/back slashes, auto-append extension)
- `InvalidateCache(name)` and `ClearCache()` for cache management

#### Choosing Between Engines

| Feature | StringTemplateEngine | RazorTemplateEngine |
|---------|---------------------|---------------------|
| **Syntax** | `{{Property}}` | Full Razor (`@Model.Property`, `@if`, `@foreach`) |
| **Conditionals** | No | Yes |
| **Loops** | No | Yes |
| **Layouts/Partials** | No | Yes |
| **Dependencies** | None | RazorLight NuGet |
| **Performance** | Fast (regex replace) | Compiled (first render slower, subsequent fast) |
| **Use case** | Simple variable replacement | Complex HTML emails with logic |

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

## Provider Projects

| Project | Channel | Backend | Status |
|---------|---------|---------|--------|
| `Birko.Messaging.Razor` | Templates | RazorLight (Razor syntax) | ✅ Implemented |
| `Birko.Messaging.SendGrid` | Email | SendGrid API | Planned |
| `Birko.Messaging.Mailgun` | Email | Mailgun API | Planned |
| `Birko.Messaging.Twilio` | SMS | Twilio API | Planned |
| `Birko.Messaging.Firebase` | Push | Firebase Cloud Messaging | Planned |
| `Birko.Messaging.Apple` | Push | Apple Push Notification Service | Planned |

## See Also

- [Birko.Messaging CLAUDE.md](../../Birko.Messaging/CLAUDE.md)
- [Birko.Messaging.Razor CLAUDE.md](../../Birko.Messaging.Razor/CLAUDE.md)
