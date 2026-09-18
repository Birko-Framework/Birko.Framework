# Birko.Messaging.Razor

Razor template engine for the Birko Messaging framework. Provides full Razor syntax support for rendering rich HTML email and message templates, replacing `StringTemplateEngine` for complex templates that need conditionals, loops, and layouts.

## Features

- **Full Razor syntax** — `@if`, `@foreach`, `@model`, partial views, layouts
- **Strongly-typed models** — `@model OrderConfirmation` with IntelliSense in IDE
- **Inline templates** — Render Razor strings directly without files
- **File-based templates** — Load `.cshtml` templates from disk
- **Template caching** — Compiled templates cached for reuse (configurable)
- **IMessageTemplate support** — Automatic file lookup by name, fallback to inline BodyTemplate
- **Directory traversal protection** — Prevents path escape from template base directory
- **Configurable encoding** — UTF-8 default, supports any encoding for file reads

## Dependencies

- **Birko.Messaging** — `ITemplateEngine`, `IMessageTemplate`, `TemplateRenderException`
- **RazorLight** — NuGet package (added by consuming project)

## Usage

### Inline Razor Templates

```csharp
var engine = new RazorTemplateEngine();

var html = await engine.RenderAsync(
    "@model dynamic\n<h1>Hello @Model.Name!</h1>\n<p>Your order @Model.OrderId is ready.</p>",
    new { Name = "John", OrderId = "ORD-123" });
// Result: <h1>Hello John!</h1><p>Your order ORD-123 is ready.</p>
```

### Strongly-Typed Models

```csharp
public class OrderConfirmation
{
    public string CustomerName { get; set; }
    public List<OrderItem> Items { get; set; }
    public decimal Total { get; set; }
}

var html = await engine.RenderAsync(@"
@model OrderConfirmation
<h1>Thank you, @Model.CustomerName!</h1>
<ul>
@foreach (var item in Model.Items)
{
    <li>@item.Name — @item.Price.ToString(""C"")</li>
}
</ul>
<p><strong>Total: @Model.Total.ToString(""C"")</strong></p>",
    order);
```

### File-Based Templates

```csharp
var engine = new RazorTemplateEngine(new RazorTemplateOptions
{
    TemplateBasePath = "/app/templates",
    EnableCaching = true
});

// Renders /app/templates/OrderConfirmation.cshtml
var html = await engine.RenderFileAsync("OrderConfirmation", order);

// Subdirectories supported
var welcome = await engine.RenderFileAsync("Emails/Welcome", user);
```

### With IMessageTemplate

```csharp
// File-based templates take priority when TemplateBasePath is configured.
// Falls back to messageTemplate.BodyTemplate if file not found.
var html = await engine.RenderAsync(messageTemplate, model);
```

### With IEmailSender

```csharp
var razorEngine = new RazorTemplateEngine(new RazorTemplateOptions
{
    TemplateBasePath = "/app/templates/emails"
});

var body = await razorEngine.RenderFileAsync("InvoiceEmail", invoice);

var email = new EmailMessage
{
    Subject = $"Invoice #{invoice.Number}",
    Body = body,
    IsHtml = true,
    To = { new MessageAddress(customer.Email, customer.Name) }
};

await emailSender.SendAsync(email);
```

## API Reference

### RazorTemplateEngine

Implements `ITemplateEngine` and `IDisposable`.

| Method | Description |
|--------|-------------|
| `RenderAsync(string, object, CancellationToken)` | Render inline Razor template string |
| `RenderAsync(IMessageTemplate, object, CancellationToken)` | Render from IMessageTemplate (file lookup + inline fallback) |
| `RenderFileAsync(string, object, CancellationToken)` | Render .cshtml file by name |
| `InvalidateFileCache(string)` | Invalidate cached file template |

### RazorTemplateOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TemplateBasePath` | `string?` | `null` | Base directory for .cshtml files |
| `FileExtension` | `string` | `".cshtml"` | Template file extension |
| `EnableCaching` | `bool` | `true` | Cache compiled templates |
| `FileEncoding` | `Encoding` | `UTF8` | File reading encoding |
| `DefaultNamespaces` | `string[]` | `[]` | Namespaces auto-imported in templates |

### RazorFileTemplateProvider

| Method | Description |
|--------|-------------|
| `GetTemplateAsync(string, CancellationToken)` | Load template content from disk |
| `InvalidateCache(string)` | Remove single entry from cache |
| `ClearCache()` | Clear all cached templates |

## License

Part of the Birko Framework. See [License.md](License.md).
