using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue;
using Birko.MessageQueue.InMemory;
using Birko.MessageQueue.Mqtt;
using Birko.MessageQueue.Serialization;

namespace Birko.Framework.Examples.MessageQueue
{
    /// <summary>
    /// Examples demonstrating the Birko.MessageQueue framework.
    /// </summary>
    public static class MessageQueueExamples
    {
        /// <summary>
        /// Basic pub/sub with the InMemory queue.
        /// </summary>
        public static async Task RunPubSubExample()
        {
            ExampleOutput.WriteHeader("Pub/Sub with InMemory Queue");
            ExampleOutput.WriteLine();

            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            ExampleOutput.WriteSuccess($"Connected: {queue.IsConnected}");
            ExampleOutput.WriteLine();

            // Subscribe to a topic
            var received = 0;
            var tcs = new TaskCompletionSource<QueueMessage>();

            var subscription = await queue.Consumer.SubscribeAsync("orders", async (msg, ct) =>
            {
                Interlocked.Increment(ref received);
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            });

            ExampleOutput.WriteInfo("Subscribed", "orders topic");

            // Publish a message
            var message = new QueueMessage
            {
                Body = "Order #12345 placed",
                Headers = new MessageHeaders
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    ContentType = "text/plain"
                }
            };
            message.Headers.Custom["priority"] = "high";

            await queue.Producer.SendAsync("orders", message, CancellationToken.None);
            ExampleOutput.WriteInfo("Published", "1 message to 'orders'");

            // Wait for delivery
            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            ExampleOutput.WriteLine();
            ExampleOutput.WriteInfo("Received", result.Body);
            ExampleOutput.WriteInfo("Message ID", result.Id.ToString());
            ExampleOutput.WriteInfo("Created", result.CreatedAt.ToString("O"));
            ExampleOutput.WriteInfo("Correlation", result.Headers.CorrelationId ?? "n/a");
            ExampleOutput.WriteInfo("Custom[priority]", result.Headers.Custom.GetValueOrDefault("priority", "n/a"));
            ExampleOutput.WriteInfo("Delivered", $"{received} message(s)");

            // Unsubscribe
            await subscription.UnsubscribeAsync();
            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess($"Unsubscribed (active: {subscription.IsActive})");

            await queue.DisconnectAsync();
            ExampleOutput.WriteSuccess($"Disconnected: {!queue.IsConnected}");
        }

        /// <summary>
        /// Typed message serialization.
        /// </summary>
        public static async Task RunTypedMessagesExample()
        {
            ExampleOutput.WriteHeader("Typed Messages with Serialization");
            ExampleOutput.WriteLine();

            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var tcs = new TaskCompletionSource<QueueMessage>();
            await queue.Consumer.SubscribeAsync("events", async (msg, ct) =>
            {
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            });

            // Send a typed object — automatically serialized to JSON
            var sensorReading = new SensorReading
            {
                SensorId = "TEMP-001",
                Value = 23.5,
                Unit = "°C",
                Timestamp = DateTime.UtcNow
            };

            await queue.Producer.SendAsync("events", sensorReading);
            ExampleOutput.WriteInfo("Sent", $"SensorReading for {sensorReading.SensorId}");

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            ExampleOutput.WriteInfo("Raw body", received.Body);

            // Deserialize back
            var serializer = new JsonMessageSerializer();
            var restored = serializer.Deserialize<SensorReading>(received.Body);
            ExampleOutput.WriteLine();
            ExampleOutput.WriteInfo("SensorId", restored!.SensorId);
            ExampleOutput.WriteInfo("Value", $"{restored.Value} {restored.Unit}");
            ExampleOutput.WriteInfo("Timestamp", restored.Timestamp.ToString("O"));

            await queue.DisconnectAsync();
            ExampleOutput.WriteSuccess("Done");
        }

        /// <summary>
        /// Encrypting message serializer (decorator pattern).
        /// </summary>
        public static void RunEncryptionExample()
        {
            ExampleOutput.WriteHeader("Encrypting Message Serializer");
            ExampleOutput.WriteLine();

            // Simple base64 "encryption" for demo (replace with AES in production)
            static string encrypt(string plain) =>
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
            static string decrypt(string cipher) =>
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipher));

            var inner = new JsonMessageSerializer();
            var encrypted = new EncryptingMessageSerializer(inner, encrypt, decrypt);

            ExampleOutput.WriteInfo("Inner content type", inner.ContentType);
            ExampleOutput.WriteInfo("Encrypted content type", encrypted.ContentType);
            ExampleOutput.WriteLine();

            var payload = new SensorReading
            {
                SensorId = "SEC-007",
                Value = 42.0,
                Unit = "dB",
                Timestamp = DateTime.UtcNow
            };

            // Serialize with inner (plain JSON)
            var json = inner.Serialize(payload);
            ExampleOutput.WriteInfo("Plain JSON", json);

            // Serialize with encrypting wrapper
            var cipher = encrypted.Serialize(payload);
            ExampleOutput.WriteInfo("Encrypted", cipher);
            ExampleOutput.WriteDim("(Base64 encoded — no readable JSON visible)");

            // Round-trip decrypt
            var restored = encrypted.Deserialize<SensorReading>(cipher);
            ExampleOutput.WriteLine();
            ExampleOutput.WriteInfo("Decrypted SensorId", restored!.SensorId);
            ExampleOutput.WriteInfo("Decrypted Value", $"{restored.Value} {restored.Unit}");
            ExampleOutput.WriteSuccess("Round-trip preserved data integrity");
        }

        /// <summary>
        /// Message fingerprinting for deduplication.
        /// </summary>
        public static void RunFingerprintExample()
        {
            ExampleOutput.WriteHeader("Message Fingerprint (Deduplication)");
            ExampleOutput.WriteLine();

            // Same content produces same fingerprint
            var fp1 = MessageFingerprint.Compute("Hello, World!");
            var fp2 = MessageFingerprint.Compute("Hello, World!");
            var fp3 = MessageFingerprint.Compute("Different content");

            ExampleOutput.WriteInfo("Fingerprint 1", fp1);
            ExampleOutput.WriteInfo("Fingerprint 2", fp2);
            ExampleOutput.WriteInfo("Fingerprint 3", fp3);
            ExampleOutput.WriteInfo("1 == 2", (fp1 == fp2).ToString());
            ExampleOutput.WriteInfo("1 == 3", (fp1 == fp3).ToString());
            ExampleOutput.WriteLine();

            // QueueMessage fingerprinting
            var msg = new QueueMessage { Body = "Order #999" };
            var msgFp = MessageFingerprint.Compute(msg);
            ExampleOutput.WriteInfo("Message fingerprint", msgFp);

            // Destination-scoped fingerprinting
            var scopedA = MessageFingerprint.Compute("orders", msg.Body);
            var scopedB = MessageFingerprint.Compute("events", msg.Body);
            ExampleOutput.WriteInfo("Scoped (orders)", scopedA);
            ExampleOutput.WriteInfo("Scoped (events)", scopedB);
            ExampleOutput.WriteInfo("Same body, different scope", (scopedA != scopedB).ToString());
            ExampleOutput.WriteSuccess("Fingerprints enable idempotent message processing");
        }

        /// <summary>
        /// MQTT topic utilities (validation and wildcard matching).
        /// </summary>
        public static void RunMqttTopicsExample()
        {
            ExampleOutput.WriteHeader("MQTT Topic Utilities");
            ExampleOutput.WriteLine();

            // Publish topic validation (no wildcards allowed)
            ExampleOutput.WriteDim("Publish topic validation:");
            ExampleOutput.WriteInfo("sensors/temp", MqttTopic.IsValidPublishTopic("sensors/temp").ToString());
            ExampleOutput.WriteInfo("sensors/+/temp", MqttTopic.IsValidPublishTopic("sensors/+/temp").ToString());
            ExampleOutput.WriteInfo("sensors/#", MqttTopic.IsValidPublishTopic("sensors/#").ToString());
            ExampleOutput.WriteLine();

            // Subscribe filter validation (wildcards OK)
            ExampleOutput.WriteDim("Subscribe filter validation:");
            ExampleOutput.WriteInfo("sensors/+/temp", MqttTopic.IsValidSubscribeFilter("sensors/+/temp").ToString());
            ExampleOutput.WriteInfo("sensors/#", MqttTopic.IsValidSubscribeFilter("sensors/#").ToString());
            ExampleOutput.WriteInfo("#", MqttTopic.IsValidSubscribeFilter("#").ToString());
            ExampleOutput.WriteInfo("sensors/ab+cd", MqttTopic.IsValidSubscribeFilter("sensors/ab+cd").ToString());
            ExampleOutput.WriteLine();

            // Wildcard matching
            ExampleOutput.WriteDim("Wildcard matching:");
            ExampleOutput.WriteInfo("sensors/+/temp vs sensors/room1/temp", MqttTopic.Matches("sensors/+/temp", "sensors/room1/temp").ToString());
            ExampleOutput.WriteInfo("sensors/# vs sensors/room1/temp", MqttTopic.Matches("sensors/#", "sensors/room1/temp").ToString());
            ExampleOutput.WriteInfo("sensors/# vs sensors", MqttTopic.Matches("sensors/#", "sensors").ToString());
            ExampleOutput.WriteInfo("# vs anything/at/all", MqttTopic.Matches("#", "anything/at/all").ToString());
            ExampleOutput.WriteSuccess("Topic utilities help validate and route MQTT messages");
        }

        /// <summary>
        /// Manual acknowledgement mode.
        /// </summary>
        public static async Task RunManualAckExample()
        {
            ExampleOutput.WriteHeader("Manual Acknowledgement");
            ExampleOutput.WriteLine();

            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var tcs = new TaskCompletionSource<QueueMessage>();

            await queue.Consumer.SubscribeAsync("tasks", async (msg, ct) =>
            {
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            }, new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

            ExampleOutput.WriteInfo("AckMode", "ManualAck");

            await queue.Producer.SendAsync("tasks", new QueueMessage { Body = "Process invoice #42" }, CancellationToken.None);
            ExampleOutput.WriteInfo("Sent", "Process invoice #42");

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            ExampleOutput.WriteInfo("Received", received.Body);
            ExampleOutput.WriteInfo("Message ID", received.Id.ToString());

            // Acknowledge after processing
            await queue.Consumer.AcknowledgeAsync(received.Id);
            ExampleOutput.WriteSuccess("Message acknowledged");

            await queue.DisconnectAsync();
        }
    }

    // --- Example model ---

    public class SensorReading
    {
        public string SensorId { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
