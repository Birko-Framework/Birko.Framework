using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Messaging;
using Birko.Messaging.Razor;
using Birko.Messaging.Templates;

namespace Birko.Framework.Examples.Messaging
{
    public static class RazorTemplateExamples
    {
        /// <summary>
        /// Inline Razor template rendering with simple model properties.
        /// </summary>
        public static async Task RunInlineRenderExample()
        {
            ExampleOutput.WriteLine("=== Razor Inline Template Example ===\n");

            using var engine = new RazorTemplateEngine();

            // Simple property access
            ExampleOutput.WriteHeader("Simple Properties");
            var result = await engine.RenderAsync(
                "Hello @Model.Name, your order #@Model.OrderId is confirmed.",
                new { Name = "Alice", OrderId = "ORD-2026-001" });
            ExampleOutput.WriteInfo("Template", "Hello @Model.Name, your order #@Model.OrderId is confirmed.");
            ExampleOutput.WriteInfo("Result", result);

            // Nested properties
            ExampleOutput.WriteHeader("Nested Properties");
            result = await engine.RenderAsync(
                "Dear @Model.Customer.Name from @Model.Customer.City",
                new { Customer = new { Name = "Bob", City = "Prague" } });
            ExampleOutput.WriteInfo("Result", result);

            // Expression formatting
            ExampleOutput.WriteHeader("Formatted Expressions");
            result = await engine.RenderAsync(
                "Total: @Model.Total.ToString(\"C\") on @Model.Date.ToString(\"d\")",
                new { Total = 149.99m, Date = new DateTime(2026, 3, 17) });
            ExampleOutput.WriteInfo("Result", result);

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Razor conditionals and loops for complex HTML email templates.
        /// </summary>
        public static async Task RunConditionalAndLoopExample()
        {
            ExampleOutput.WriteLine("=== Razor Conditionals & Loops Example ===\n");

            using var engine = new RazorTemplateEngine();

            // Conditional
            ExampleOutput.WriteHeader("Conditional (@if)");
            var template = "@if (Model.IsVip) { <b>VIP Customer</b> } else { <span>Regular Customer</span> }";
            var vipResult = await engine.RenderAsync(template, new { IsVip = true });
            var regResult = await engine.RenderAsync(template, new { IsVip = false });
            ExampleOutput.WriteInfo("VIP=true", vipResult.Trim());
            ExampleOutput.WriteInfo("VIP=false", regResult.Trim());

            // Loop
            ExampleOutput.WriteHeader("Loop (@foreach)");
            var loopTemplate = @"<ul>
@foreach (var item in Model.Items)
{
    <li>@item.Name — @item.Price.ToString(""C"")</li>
}
</ul>";
            var items = new[]
            {
                new { Name = "Widget", Price = 9.99m },
                new { Name = "Gadget", Price = 24.50m },
                new { Name = "Doohickey", Price = 3.75m }
            };
            var result = await engine.RenderAsync(loopTemplate, new { Items = items });
            ExampleOutput.WriteInfo("Rendered", result.Trim());

            // Combined: order confirmation email body
            ExampleOutput.WriteHeader("Combined: Order Email");
            var emailTemplate = @"<h1>Order Confirmation</h1>
<p>Dear @Model.CustomerName,</p>
<p>Thank you for your order!</p>
<table>
@foreach (var line in Model.Lines)
{
    <tr><td>@line.Product</td><td>@line.Qty</td><td>@line.Price.ToString(""C"")</td></tr>
}
</table>
<p><strong>Total: @Model.Total.ToString(""C"")</strong></p>
@if (Model.IsVip)
{
    <p>Your VIP discount has been applied.</p>
}";
            result = await engine.RenderAsync(emailTemplate, new
            {
                CustomerName = "Alice",
                Lines = new[]
                {
                    new { Product = "Laptop", Qty = 1, Price = 999.00m },
                    new { Product = "Mouse", Qty = 2, Price = 25.00m }
                },
                Total = 1049.00m,
                IsVip = true
            });
            ExampleOutput.WriteInfo("Email HTML", result.Trim());

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// File-based .cshtml template rendering from disk.
        /// </summary>
        public static async Task RunFileTemplateExample()
        {
            ExampleOutput.WriteLine("=== Razor File Template Example ===\n");

            // Create a temp directory with sample templates
            var tempDir = Path.Combine(Path.GetTempPath(), $"birko_razor_demo_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "Emails"));

            try
            {
                // Write sample templates to disk
                File.WriteAllText(Path.Combine(tempDir, "Welcome.cshtml"),
                    "<h1>Welcome, @Model.Name!</h1>\n<p>Your account has been created.</p>");

                File.WriteAllText(Path.Combine(tempDir, "Emails", "Invoice.cshtml"),
                    @"<h1>Invoice #@Model.Number</h1>
<p>Dear @Model.Customer,</p>
<p>Amount due: @Model.Amount.ToString(""C"")</p>
@if (Model.IsPaid)
{
    <p style=""color:green"">PAID</p>
}
else
{
    <p style=""color:red"">Payment pending</p>
}");

                ExampleOutput.WriteInfo("Template directory", tempDir);
                ExampleOutput.WriteInfo("Files", "Welcome.cshtml, Emails/Invoice.cshtml");

                // Create engine with file system support
                using var engine = new RazorTemplateEngine(new RazorTemplateOptions
                {
                    TemplateBasePath = tempDir,
                    EnableCaching = true
                });

                // Render root-level template
                ExampleOutput.WriteHeader("Root Template: Welcome");
                var result = await engine.RenderFileAsync("Welcome", new { Name = "Charlie" });
                ExampleOutput.WriteInfo("Rendered", result.Trim());

                // Render subdirectory template
                ExampleOutput.WriteHeader("Subdirectory Template: Emails/Invoice");
                result = await engine.RenderFileAsync("Emails/Invoice", new
                {
                    Number = "INV-2026-042",
                    Customer = "Alice Corp",
                    Amount = 2499.00m,
                    IsPaid = false
                });
                ExampleOutput.WriteInfo("Rendered", result.Trim());

                // Cache invalidation
                ExampleOutput.WriteHeader("Cache Invalidation");
                File.WriteAllText(Path.Combine(tempDir, "Welcome.cshtml"),
                    "<h1>Updated welcome, @Model.Name!</h1>");
                engine.InvalidateFileCache("Welcome");
                result = await engine.RenderFileAsync("Welcome", new { Name = "Charlie" });
                ExampleOutput.WriteInfo("After invalidate", result.Trim());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// IMessageTemplate rendering with file fallback and inline BodyTemplate.
        /// </summary>
        public static async Task RunMessageTemplateExample()
        {
            ExampleOutput.WriteLine("=== Razor IMessageTemplate Example ===\n");

            var tempDir = Path.Combine(Path.GetTempPath(), $"birko_razor_mt_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Write a file-based template matching the IMessageTemplate name
                File.WriteAllText(Path.Combine(tempDir, "order-confirmation.cshtml"),
                    "<h1>Order @Model.OrderId</h1>\n<p>Thank you, @Model.Customer! (from file)</p>");

                using var engine = new RazorTemplateEngine(new RazorTemplateOptions
                {
                    TemplateBasePath = tempDir
                });

                // File-based: name matches a .cshtml file
                ExampleOutput.WriteHeader("File-Based IMessageTemplate");
                var fileTemplate = new DemoTemplate
                {
                    Name = "order-confirmation",
                    Subject = "Order @Model.OrderId",
                    BodyTemplate = "<p>Fallback: Order @Model.OrderId for @Model.Customer</p>",
                    IsHtml = true
                };
                var result = await engine.RenderAsync(fileTemplate, new { OrderId = "ORD-99", Customer = "Bob" });
                ExampleOutput.WriteInfo("Source", "File (order-confirmation.cshtml)");
                ExampleOutput.WriteInfo("Rendered", result.Trim());

                // Inline fallback: name doesn't match any file
                ExampleOutput.WriteHeader("Inline Fallback IMessageTemplate");
                var inlineTemplate = new DemoTemplate
                {
                    Name = "no-file-exists",
                    Subject = "Notification",
                    BodyTemplate = "<p>Hello @Model.Name, this is rendered from BodyTemplate (inline).</p>",
                    IsHtml = true
                };
                result = await engine.RenderAsync(inlineTemplate, new { Name = "Charlie" });
                ExampleOutput.WriteInfo("Source", "Inline BodyTemplate (file not found)");
                ExampleOutput.WriteInfo("Rendered", result.Trim());

                // Without file provider — always uses inline
                ExampleOutput.WriteHeader("No File Provider (Inline Only)");
                using var inlineEngine = new RazorTemplateEngine();
                result = await inlineEngine.RenderAsync(fileTemplate, new { OrderId = "ORD-100", Customer = "Diana" });
                ExampleOutput.WriteInfo("Source", "Inline BodyTemplate (no TemplateBasePath)");
                ExampleOutput.WriteInfo("Rendered", result.Trim());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// RazorTemplateOptions configuration demo.
        /// </summary>
        public static void RunOptionsExample()
        {
            ExampleOutput.WriteLine("=== Razor Template Options Example ===\n");

            // Default options
            var defaults = new RazorTemplateOptions();
            ExampleOutput.WriteHeader("Default Options");
            ExampleOutput.WriteInfo("TemplateBasePath", defaults.TemplateBasePath ?? "(null — inline only)");
            ExampleOutput.WriteInfo("FileExtension", defaults.FileExtension);
            ExampleOutput.WriteInfo("EnableCaching", defaults.EnableCaching.ToString());
            ExampleOutput.WriteInfo("FileEncoding", defaults.FileEncoding.EncodingName);
            ExampleOutput.WriteInfo("DefaultNamespaces", defaults.DefaultNamespaces.Length == 0 ? "(none)" : string.Join(", ", defaults.DefaultNamespaces));

            // Custom options
            var custom = new RazorTemplateOptions
            {
                TemplateBasePath = "/app/templates/emails",
                FileExtension = ".cshtml",
                EnableCaching = true,
                FileEncoding = System.Text.Encoding.UTF8,
                DefaultNamespaces = new[] { "System.Linq", "System.Globalization" }
            };
            ExampleOutput.WriteHeader("Custom Options");
            ExampleOutput.WriteInfo("TemplateBasePath", custom.TemplateBasePath!);
            ExampleOutput.WriteInfo("FileExtension", custom.FileExtension);
            ExampleOutput.WriteInfo("EnableCaching", custom.EnableCaching.ToString());
            ExampleOutput.WriteInfo("FileEncoding", custom.FileEncoding.EncodingName);
            ExampleOutput.WriteInfo("DefaultNamespaces", string.Join(", ", custom.DefaultNamespaces));

            ExampleOutput.WriteLine("\nNote: consuming project must add:");
            ExampleOutput.WriteDim("  <PackageReference Include=\"RazorLight\" Version=\"2.*\" />");
            ExampleOutput.WriteDim("  <PreserveCompilationContext>true</PreserveCompilationContext>");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Error handling: TemplateRenderException for invalid templates.
        /// </summary>
        public static async Task RunErrorHandlingExample()
        {
            ExampleOutput.WriteLine("=== Razor Error Handling Example ===\n");

            using var engine = new RazorTemplateEngine();

            // Runtime exception in template
            ExampleOutput.WriteHeader("Template Runtime Error");
            try
            {
                await engine.RenderAsync("@{ throw new System.InvalidOperationException(\"Something went wrong\"); }", new { });
            }
            catch (TemplateRenderException ex)
            {
                ExampleOutput.WriteError($"TemplateRenderException: {ex.Message}");
                ExampleOutput.WriteInfo("TemplateName", ex.TemplateName);
                ExampleOutput.WriteInfo("InnerException", ex.InnerException?.GetType().Name ?? "(none)");
            }

            // Null arguments
            ExampleOutput.WriteHeader("Null Argument Validation");
            try
            {
                await engine.RenderAsync((string)null!, new { });
            }
            catch (ArgumentNullException ex)
            {
                ExampleOutput.WriteError($"ArgumentNullException: {ex.ParamName}");
            }

            try
            {
                await engine.RenderAsync("Hello", null!);
            }
            catch (ArgumentNullException ex)
            {
                ExampleOutput.WriteError($"ArgumentNullException: {ex.ParamName}");
            }

            // Disposed engine
            ExampleOutput.WriteHeader("Disposed Engine");
            var disposable = new RazorTemplateEngine();
            disposable.Dispose();
            try
            {
                await disposable.RenderAsync("test", new { });
            }
            catch (ObjectDisposedException)
            {
                ExampleOutput.WriteError("ObjectDisposedException: engine was disposed");
            }

            // File not found (RenderFileAsync without base path)
            ExampleOutput.WriteHeader("No TemplateBasePath");
            try
            {
                await engine.RenderFileAsync("SomeTemplate", new { });
            }
            catch (TemplateRenderException ex)
            {
                ExampleOutput.WriteError($"TemplateRenderException: {ex.Message}");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Comparison: StringTemplateEngine vs RazorTemplateEngine.
        /// </summary>
        public static async Task RunComparisonExample()
        {
            ExampleOutput.WriteLine("=== String vs Razor Template Engine Comparison ===\n");

            var model = new { Name = "Alice", Items = new[] { "Widget", "Gadget" }, IsVip = true };

            // StringTemplateEngine: simple replacement only
            ExampleOutput.WriteHeader("StringTemplateEngine");
            var stringEngine = new StringTemplateEngine();
            var stringResult = await stringEngine.RenderAsync("Hello {{Name}}!", model);
            ExampleOutput.WriteInfo("Template", "Hello {{Name}}!");
            ExampleOutput.WriteInfo("Result", stringResult);
            ExampleOutput.WriteDim("  No conditionals, no loops, no expressions");

            // RazorTemplateEngine: full Razor syntax
            ExampleOutput.WriteHeader("RazorTemplateEngine");
            using var razorEngine = new RazorTemplateEngine();
            var razorResult = await razorEngine.RenderAsync(
                "Hello @Model.Name! @if (Model.IsVip) { <b>VIP</b> } Items: @string.Join(\", \", Model.Items)", model);
            ExampleOutput.WriteInfo("Template", "Hello @Model.Name! @if (Model.IsVip) { <b>VIP</b> } Items: @string.Join(\", \", Model.Items)");
            ExampleOutput.WriteInfo("Result", razorResult.Trim());
            ExampleOutput.WriteDim("  Full C# expressions, conditionals, loops, layouts");

            ExampleOutput.WriteHeader("When to Use Which");
            ExampleOutput.WriteLine("  StringTemplateEngine:");
            ExampleOutput.WriteDim("    Simple variable replacement ({{Name}})");
            ExampleOutput.WriteDim("    No external dependencies");
            ExampleOutput.WriteDim("    Fast (regex-based, no compilation)");
            ExampleOutput.WriteLine("  RazorTemplateEngine:");
            ExampleOutput.WriteDim("    Complex HTML emails with conditionals/loops");
            ExampleOutput.WriteDim("    Strongly-typed models (@Model.Property)");
            ExampleOutput.WriteDim("    File-based .cshtml templates");
            ExampleOutput.WriteDim("    Requires RazorLight NuGet package");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        private class DemoTemplate : IMessageTemplate
        {
            public string Name { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string BodyTemplate { get; set; } = string.Empty;
            public bool IsHtml { get; set; }
        }
    }
}
