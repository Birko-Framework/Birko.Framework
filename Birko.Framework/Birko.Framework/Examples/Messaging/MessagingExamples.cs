using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Messaging;
using Birko.Messaging.Email;
using Birko.Messaging.Sms;
using Birko.Messaging.Push;
using Birko.Messaging.Templates;

namespace Birko.Framework.Examples.Messaging
{
    public static class MessagingExamples
    {
        /// <summary>
        /// EmailSettings and SmtpEmailSender configuration.
        /// </summary>
        public static void RunEmailSettingsExample()
        {
            ExampleOutput.WriteLine("=== Email Settings Example ===\n");

            // Basic SMTP settings (inherits from RemoteSettings)
            var settings = new EmailSettings("smtp.example.com", 587, "user@example.com", "password")
            {
                UseSecure = true,       // SSL/TLS (default: true)
                Timeout = 30000,        // 30 seconds (default)
                DefaultFrom = new MessageAddress("noreply@example.com", "My Application")
            };

            ExampleOutput.WriteInfo("Host", settings.Location!);
            ExampleOutput.WriteInfo("Port", settings.Port.ToString());
            ExampleOutput.WriteInfo("UserName", settings.UserName);
            ExampleOutput.WriteInfo("UseSecure", settings.UseSecure.ToString());
            ExampleOutput.WriteInfo("Timeout", $"{settings.Timeout}ms");
            ExampleOutput.WriteInfo("DefaultFrom", settings.DefaultFrom?.ToString() ?? "(none)");

            ExampleOutput.WriteLine("\nEmailSettings extends RemoteSettings:");
            ExampleOutput.WriteLine("  Settings (Location, Name)");
            ExampleOutput.WriteLine("    -> PasswordSettings (Password)");
            ExampleOutput.WriteLine("      -> RemoteSettings (UserName, Port, UseSecure)");
            ExampleOutput.WriteLine("        -> EmailSettings (Timeout, DefaultFrom)");

            // LoadFrom copies all settings
            var copy = new EmailSettings();
            copy.LoadFrom(settings);
            ExampleOutput.WriteLine($"\nLoadFrom copy - Host: {copy.Location}, Port: {copy.Port}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Building and inspecting EmailMessage objects.
        /// </summary>
        public static void RunEmailMessageExample()
        {
            ExampleOutput.WriteLine("=== Email Message Example ===\n");

            // Simple email
            var simple = new EmailMessage
            {
                From = new MessageAddress("sender@example.com", "Sender"),
                Recipients = new[] { new MessageAddress("user@example.com", "User") },
                Subject = "Hello!",
                Body = "Plain text body",
                IsHtml = false
            };

            ExampleOutput.WriteHeader("Simple Email");
            PrintEmail(simple);

            // Full-featured email
            var full = new EmailMessage
            {
                Id = "msg-001",
                From = new MessageAddress("noreply@company.com", "Company"),
                Recipients = new[]
                {
                    new MessageAddress("alice@example.com", "Alice"),
                    new MessageAddress("bob@example.com", "Bob")
                },
                Cc = new[] { new MessageAddress("manager@company.com") },
                Bcc = new[] { new MessageAddress("archive@company.com") },
                ReplyTo = new MessageAddress("support@company.com", "Support"),
                Subject = "Invoice #INV-2026-001",
                Body = "<h1>Invoice</h1><p>Please find your invoice attached.</p>",
                IsHtml = true,
                PlainTextBody = "Please find your invoice attached.",
                Priority = MessagePriority.High,
            };
            full.Headers["X-Invoice-Id"] = "INV-2026-001";
            full.Metadata["campaign"] = "monthly-billing";

            ExampleOutput.WriteHeader("Full Email");
            PrintEmail(full);

            ExampleOutput.WriteLine("\nMessagePriority levels: Low, Normal (default), High");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// SmtpEmailSender: sending emails via SMTP (demonstrates API without real server).
        /// </summary>
        public static async Task RunSmtpSenderExample()
        {
            ExampleOutput.WriteLine("=== SMTP Email Sender Example ===\n");

            var settings = new EmailSettings("localhost", 25)
            {
                UseSecure = false,
                DefaultFrom = new MessageAddress("app@localhost", "Demo App")
            };

            using var sender = new SmtpEmailSender(settings);

            // Convenience method (builds EmailMessage internally)
            ExampleOutput.WriteLine("Convenience SendAsync(from, to, subject, body, isHtml):");
            var result = await sender.SendAsync(
                new MessageAddress("from@localhost"),
                new MessageAddress("to@localhost"),
                "Test Subject",
                "<p>Hello World</p>",
                isHtml: true);
            PrintResult("Convenience send", result);

            // Full message
            ExampleOutput.WriteLine("\nFull EmailMessage SendAsync:");
            var email = new EmailMessage
            {
                Recipients = new[] { new MessageAddress("user@localhost") },
                Subject = "Using DefaultFrom",
                Body = "From address comes from EmailSettings.DefaultFrom"
            };
            result = await sender.SendAsync(email);
            PrintResult("Full message send", result);

            // Batch send
            ExampleOutput.WriteLine("\nBatch SendBatchAsync:");
            var messages = new[]
            {
                new EmailMessage
                {
                    Recipients = new[] { new MessageAddress("a@localhost") },
                    Subject = "Batch 1", Body = "First"
                },
                new EmailMessage
                {
                    Recipients = new[] { new MessageAddress("b@localhost") },
                    Subject = "Batch 2", Body = "Second"
                }
            };
            var results = await sender.SendBatchAsync(messages);
            for (int i = 0; i < results.Count; i++)
            {
                PrintResult($"  Batch [{i}]", results[i]);
            }

            // Validation: no recipients
            ExampleOutput.WriteLine("\nValidation - no recipients:");
            result = await sender.SendAsync(new EmailMessage { Subject = "No recipients", Body = "test" });
            PrintResult("No recipients", result);

            // Validation: no sender
            ExampleOutput.WriteLine("\nValidation - no sender (clear DefaultFrom):");
            var noDefaultSettings = new EmailSettings("localhost", 25) { UseSecure = false };
            using var senderNoDefault = new SmtpEmailSender(noDefaultSettings);
            result = await senderNoDefault.SendAsync(new EmailMessage
            {
                Recipients = new[] { new MessageAddress("user@localhost") },
                Subject = "No from", Body = "test"
            });
            PrintResult("No sender", result);

            ExampleOutput.WriteLine("\nNote: SmtpEmailSender is IDisposable (wraps SmtpClient).");
            ExampleOutput.WriteLine("Delivery errors return MessageResult.Failed() — no exceptions thrown.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// MessageAddress: recipient representation with equality.
        /// </summary>
        public static void RunMessageAddressExample()
        {
            ExampleOutput.WriteLine("=== Message Address Example ===\n");

            var email = new MessageAddress("user@example.com", "John Doe");
            var phone = new MessageAddress("+1234567890");
            var token = new MessageAddress("device-token-abc123");

            ExampleOutput.WriteInfo("Email", email.ToString());
            ExampleOutput.WriteInfo("Phone", phone.ToString());
            ExampleOutput.WriteInfo("Device Token", token.ToString());

            // Case-insensitive equality
            var upper = new MessageAddress("USER@EXAMPLE.COM");
            var lower = new MessageAddress("user@example.com");
            ExampleOutput.WriteLine($"\nCase-insensitive equality:");
            ExampleOutput.WriteInfo("'USER@EXAMPLE.COM' == 'user@example.com'", upper.Equals(lower).ToString());
            ExampleOutput.WriteInfo("Same hash code", (upper.GetHashCode() == lower.GetHashCode()).ToString());

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// StringTemplateEngine: {{placeholder}} replacement with nested properties.
        /// </summary>
        public static async Task RunTemplateEngineExample()
        {
            ExampleOutput.WriteLine("=== Template Engine Example ===\n");

            var engine = new StringTemplateEngine();

            // Simple placeholders
            ExampleOutput.WriteHeader("Simple Placeholders");
            var result = await engine.RenderAsync(
                "Hello {{Name}}, welcome to {{AppName}}!",
                new { Name = "Alice", AppName = "Birko" });
            ExampleOutput.WriteInfo("Template", "Hello {{Name}}, welcome to {{AppName}}!");
            ExampleOutput.WriteInfo("Result", result);

            // Nested properties
            ExampleOutput.WriteHeader("Nested Properties");
            var model = new
            {
                Customer = new { Name = "Bob", Address = new { City = "Prague", Country = "CZ" } },
                Order = new { Id = "ORD-123", Total = 99.50m }
            };
            result = await engine.RenderAsync(
                "Dear {{Customer.Name}} from {{Customer.Address.City}}, order {{Order.Id}} total: {{Order.Total}}",
                model);
            ExampleOutput.WriteInfo("Result", result);

            // IMessageTemplate
            ExampleOutput.WriteHeader("IMessageTemplate");
            var template = new InvoiceTemplate();
            ExampleOutput.WriteInfo("Template Name", template.Name);
            ExampleOutput.WriteInfo("Subject", template.Subject);
            ExampleOutput.WriteInfo("IsHtml", template.IsHtml.ToString());

            var body = await engine.RenderAsync(template,
                new { CustomerName = "Charlie", InvoiceNumber = "INV-001", Amount = "$149.99" });
            ExampleOutput.WriteInfo("Rendered Body", body);

            // Subject can be rendered separately
            var subject = await engine.RenderAsync(template.Subject,
                new { CustomerName = "Charlie", InvoiceNumber = "INV-001", Amount = "$149.99" });
            ExampleOutput.WriteInfo("Rendered Subject", subject);

            // Missing property throws TemplateRenderException
            ExampleOutput.WriteHeader("Error Handling");
            try
            {
                await engine.RenderAsync("Hello {{Missing}}!", new { Name = "test" });
            }
            catch (TemplateRenderException ex)
            {
                ExampleOutput.WriteError($"TemplateRenderException: {ex.Message}");
                ExampleOutput.WriteInfo("TemplateName", ex.TemplateName);
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// SMS and Push message types (interface only — no sender implementation in core).
        /// </summary>
        public static void RunSmsAndPushExample()
        {
            ExampleOutput.WriteLine("=== SMS & Push Messages Example ===\n");

            // SMS message
            ExampleOutput.WriteHeader("SMS Message");
            var sms = new SmsMessage
            {
                From = new MessageAddress("+1234567890"),
                Recipients = new[] { new MessageAddress("+0987654321") },
                Body = "Your verification code is 847293"
            };
            ExampleOutput.WriteInfo("From", sms.From?.ToString() ?? "(none)");
            ExampleOutput.WriteInfo("To", sms.Recipients[0].ToString());
            ExampleOutput.WriteInfo("Body", sms.Body);
            ExampleOutput.WriteLine("\n  ISmsSender implementations (planned):");
            ExampleOutput.WriteDim("Birko.Messaging.Twilio — Twilio SMS API");

            // Push message
            ExampleOutput.WriteHeader("Push Notification");
            var push = new PushMessage
            {
                Recipients = new[] { new MessageAddress("device-token-abc") },
                Title = "New Order",
                Body = "Order #ORD-456 has been placed",
                Badge = 3,
                Sound = "default",
                ClickAction = "OPEN_ORDER",
                ImageUrl = "https://example.com/order-icon.png",
                Data = { ["orderId"] = "ORD-456", ["type"] = "order_placed" }
            };
            ExampleOutput.WriteInfo("Title", push.Title);
            ExampleOutput.WriteInfo("Body", push.Body);
            ExampleOutput.WriteInfo("Badge", push.Badge?.ToString() ?? "(none)");
            ExampleOutput.WriteInfo("Sound", push.Sound ?? "(none)");
            ExampleOutput.WriteInfo("ClickAction", push.ClickAction ?? "(none)");
            ExampleOutput.WriteInfo("Data keys", string.Join(", ", push.Data.Keys));
            ExampleOutput.WriteLine("\n  IPushSender implementations (planned):");
            ExampleOutput.WriteDim("Birko.Messaging.Firebase — Firebase Cloud Messaging");
            ExampleOutput.WriteDim("Birko.Messaging.Apple — Apple Push Notification Service");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// MessageResult: success/failure tracking for sent messages.
        /// </summary>
        public static void RunMessageResultExample()
        {
            ExampleOutput.WriteLine("=== Message Result Example ===\n");

            // Success
            var success = MessageResult.Succeeded("msg-abc-123");
            ExampleOutput.WriteHeader("Succeeded");
            PrintResult("Send", success);

            // Success without ID
            var successNoId = MessageResult.Succeeded();
            ExampleOutput.WriteHeader("Succeeded (no ID)");
            PrintResult("Send", successNoId);

            // Failure
            var failure = MessageResult.Failed("SMTP connection refused");
            ExampleOutput.WriteHeader("Failed");
            PrintResult("Send", failure);

            // Failure with exception
            var ex = new InvalidOperationException("Server timeout");
            var failureEx = MessageResult.Failed("Delivery failed", ex);
            ExampleOutput.WriteHeader("Failed with Exception");
            PrintResult("Send", failureEx);
            ExampleOutput.WriteInfo("Exception Type", failureEx.Exception?.GetType().Name ?? "(none)");

            ExampleOutput.WriteLine("\nPattern: senders return MessageResult, never throw for delivery errors.");
            ExampleOutput.WriteLine("Use result.Success to check, result.Error for details.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        // ── Helpers ──────────────────────────────────────────

        private static void PrintEmail(EmailMessage msg)
        {
            ExampleOutput.WriteInfo("Id", msg.Id ?? "(auto)");
            ExampleOutput.WriteInfo("From", msg.From?.ToString() ?? "(default)");
            ExampleOutput.WriteInfo("To", string.Join(", ", msg.Recipients));
            if (msg.Cc.Count > 0) ExampleOutput.WriteInfo("Cc", string.Join(", ", msg.Cc));
            if (msg.Bcc.Count > 0) ExampleOutput.WriteInfo("Bcc", string.Join(", ", msg.Bcc));
            if (msg.ReplyTo != null) ExampleOutput.WriteInfo("ReplyTo", msg.ReplyTo.ToString());
            ExampleOutput.WriteInfo("Subject", msg.Subject);
            ExampleOutput.WriteInfo("IsHtml", msg.IsHtml.ToString());
            ExampleOutput.WriteInfo("Priority", msg.Priority.ToString());
            if (msg.PlainTextBody != null) ExampleOutput.WriteInfo("PlainText", msg.PlainTextBody);
            if (msg.Headers.Count > 0) ExampleOutput.WriteInfo("Headers", string.Join(", ", msg.Headers.Keys));
            if (msg.Metadata.Count > 0) ExampleOutput.WriteInfo("Metadata", string.Join(", ", msg.Metadata.Keys));
        }

        private static void PrintResult(string label, MessageResult result)
        {
            if (result.Success)
                ExampleOutput.WriteSuccess($"{label}: OK (MessageId: {result.MessageId ?? "n/a"})");
            else
                ExampleOutput.WriteError($"{label}: FAILED — {result.Error}");
        }

        /// <summary>
        /// Sample IMessageTemplate implementation for examples.
        /// </summary>
        private class InvoiceTemplate : IMessageTemplate
        {
            public string Name => "invoice-notification";
            public string Subject => "Invoice #{{InvoiceNumber}} for {{CustomerName}}";
            public string BodyTemplate => "<p>Dear {{CustomerName}},</p><p>Your invoice #{{InvoiceNumber}} for {{Amount}} is ready.</p>";
            public bool IsHtml => true;
        }
    }
}
