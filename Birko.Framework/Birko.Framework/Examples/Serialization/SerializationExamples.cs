using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Birko.Serialization;
using Birko.Serialization.Json;
using Birko.Serialization.Xml;
using Birko.Serialization.Newtonsoft;
using Birko.Serialization.MessagePack;
using Birko.Serialization.Protobuf;
using ProtoBuf;

namespace Birko.Framework.Examples.Serialization
{
    // ── Test models ──

    [ProtoContract]
    public class SamplePerson
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Age { get; set; }

        [ProtoMember(3)]
        public string Email { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class SampleOrder
    {
        [ProtoMember(1)]
        public string OrderId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public decimal Total { get; set; }

        [ProtoMember(3)]
        public List<string> Items { get; set; } = new();

        [ProtoMember(4)]
        public bool IsPaid { get; set; }
    }

    public static class SerializationExamples
    {
        // ────────────────────────────────────────────────────────
        //  System.Text.Json
        // ────────────────────────────────────────────────────────

        public static void RunSystemJsonExample()
        {
            ExampleOutput.WriteHeader("SystemJsonSerializer (System.Text.Json)");

            ISerializer serializer = new SystemJsonSerializer();
            ExampleOutput.WriteInfo("ContentType", serializer.ContentType);
            ExampleOutput.WriteInfo("Format", serializer.Format.ToString());
            ExampleOutput.WriteLine();

            var person = new SamplePerson { Name = "Alice", Age = 30, Email = "alice@example.com" };

            // String round-trip
            var json = serializer.Serialize(person);
            ExampleOutput.WriteInfo("Serialized", json);

            var restored = serializer.Deserialize<SamplePerson>(json)!;
            ExampleOutput.WriteSuccess($"Deserialized: {restored.Name}, age {restored.Age}");
            ExampleOutput.WriteLine();

            // Byte round-trip
            var bytes = serializer.SerializeToBytes(person);
            ExampleOutput.WriteInfo("Byte size", $"{bytes.Length} bytes");

            var fromBytes = serializer.DeserializeFromBytes<SamplePerson>(bytes)!;
            ExampleOutput.WriteSuccess($"From bytes: {fromBytes.Name}, {fromBytes.Email}");
            ExampleOutput.WriteLine();

            // Custom options
            var prettySerializer = new SystemJsonSerializer(new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null // PascalCase
            });
            var prettyJson = prettySerializer.Serialize(person);
            ExampleOutput.WriteDim("Custom options (indented, PascalCase):");
            ExampleOutput.WriteLine(prettyJson);
        }

        // ────────────────────────────────────────────────────────
        //  System.Xml.Serialization
        // ────────────────────────────────────────────────────────

        public static void RunSystemXmlExample()
        {
            ExampleOutput.WriteHeader("SystemXmlSerializer (System.Xml.Serialization)");

            ISerializer serializer = new SystemXmlSerializer();
            ExampleOutput.WriteInfo("ContentType", serializer.ContentType);
            ExampleOutput.WriteInfo("Format", serializer.Format.ToString());
            ExampleOutput.WriteLine();

            var person = new SamplePerson { Name = "Bob", Age = 25, Email = "bob@example.com" };

            // String round-trip
            var xml = serializer.Serialize(person);
            ExampleOutput.WriteDim("Serialized XML:");
            ExampleOutput.WriteLine(xml);
            ExampleOutput.WriteLine();

            var restored = serializer.Deserialize<SamplePerson>(xml)!;
            ExampleOutput.WriteSuccess($"Deserialized: {restored.Name}, age {restored.Age}");
            ExampleOutput.WriteLine();

            // Byte round-trip
            var bytes = serializer.SerializeToBytes(person);
            ExampleOutput.WriteInfo("Byte size", $"{bytes.Length} bytes");

            var fromBytes = serializer.DeserializeFromBytes<SamplePerson>(bytes)!;
            ExampleOutput.WriteSuccess($"From bytes: {fromBytes.Name}, {fromBytes.Email}");
            ExampleOutput.WriteLine();

            // Custom writer settings (indented, no declaration)
            var indentedSerializer = new SystemXmlSerializer(new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            });
            var indentedXml = indentedSerializer.Serialize(person);
            ExampleOutput.WriteDim("Custom settings (indented, no declaration):");
            ExampleOutput.WriteLine(indentedXml);
        }

        // ────────────────────────────────────────────────────────
        //  Newtonsoft.Json
        // ────────────────────────────────────────────────────────

        public static void RunNewtonsoftExample()
        {
            ExampleOutput.WriteHeader("NewtonsoftJsonSerializer (Newtonsoft.Json)");

            ISerializer serializer = new NewtonsoftJsonSerializer();
            ExampleOutput.WriteInfo("ContentType", serializer.ContentType);
            ExampleOutput.WriteInfo("Format", serializer.Format.ToString());
            ExampleOutput.WriteLine();

            var order = new SampleOrder
            {
                OrderId = "ORD-001",
                Total = 149.99m,
                Items = new List<string> { "Widget", "Gadget", "Doohickey" },
                IsPaid = true
            };

            var json = serializer.Serialize(order);
            ExampleOutput.WriteInfo("Serialized", json);

            var restored = serializer.Deserialize<SampleOrder>(json)!;
            ExampleOutput.WriteSuccess($"Deserialized: {restored.OrderId}, {restored.Items.Count} items, ${restored.Total}");
            ExampleOutput.WriteLine();

            // Custom settings (indented)
            var prettySerializer = new NewtonsoftJsonSerializer(new global::Newtonsoft.Json.JsonSerializerSettings
            {
                Formatting = global::Newtonsoft.Json.Formatting.Indented
            });
            ExampleOutput.WriteDim("Indented output:");
            ExampleOutput.WriteLine(prettySerializer.Serialize(order));
        }

        // ────────────────────────────────────────────────────────
        //  MessagePack
        // ────────────────────────────────────────────────────────

        public static void RunMessagePackExample()
        {
            ExampleOutput.WriteHeader("MessagePackBinarySerializer");

            ISerializer serializer = new MessagePackBinarySerializer();
            ExampleOutput.WriteInfo("ContentType", serializer.ContentType);
            ExampleOutput.WriteInfo("Format", serializer.Format.ToString());
            ExampleOutput.WriteLine();

            var person = new SamplePerson { Name = "Charlie", Age = 35, Email = "charlie@example.com" };

            // Byte round-trip (native format)
            var bytes = serializer.SerializeToBytes(person);
            ExampleOutput.WriteInfo("Byte size", $"{bytes.Length} bytes");

            var restored = serializer.DeserializeFromBytes<SamplePerson>(bytes)!;
            ExampleOutput.WriteSuccess($"From bytes: {restored.Name}, age {restored.Age}");
            ExampleOutput.WriteLine();

            // String round-trip (Base64)
            var encoded = serializer.Serialize(person);
            ExampleOutput.WriteInfo("Base64", encoded);

            var fromString = serializer.Deserialize<SamplePerson>(encoded)!;
            ExampleOutput.WriteSuccess($"From Base64: {fromString.Name}, {fromString.Email}");
        }

        // ────────────────────────────────────────────────────────
        //  Protobuf
        // ────────────────────────────────────────────────────────

        public static void RunProtobufExample()
        {
            ExampleOutput.WriteHeader("ProtobufBinarySerializer (protobuf-net)");

            ISerializer serializer = new ProtobufBinarySerializer();
            ExampleOutput.WriteInfo("ContentType", serializer.ContentType);
            ExampleOutput.WriteInfo("Format", serializer.Format.ToString());
            ExampleOutput.WriteLine();

            var person = new SamplePerson { Name = "Diana", Age = 28, Email = "diana@example.com" };

            // Byte round-trip (native format)
            var bytes = serializer.SerializeToBytes(person);
            ExampleOutput.WriteInfo("Byte size", $"{bytes.Length} bytes");

            var restored = serializer.DeserializeFromBytes<SamplePerson>(bytes)!;
            ExampleOutput.WriteSuccess($"From bytes: {restored.Name}, age {restored.Age}");
            ExampleOutput.WriteLine();

            // String round-trip (Base64)
            var encoded = serializer.Serialize(person);
            ExampleOutput.WriteInfo("Base64", encoded);

            var fromString = serializer.Deserialize<SamplePerson>(encoded)!;
            ExampleOutput.WriteSuccess($"From Base64: {fromString.Name}, {fromString.Email}");
        }

        // ────────────────────────────────────────────────────────
        //  Format Comparison
        // ────────────────────────────────────────────────────────

        public static void RunFormatComparisonExample()
        {
            ExampleOutput.WriteHeader("Format Comparison");

            var person = new SamplePerson { Name = "Eve", Age = 42, Email = "eve@example.com" };

            ISerializer[] serializers =
            {
                new SystemJsonSerializer(),
                new SystemXmlSerializer(),
                new NewtonsoftJsonSerializer(),
                new MessagePackBinarySerializer(),
                new ProtobufBinarySerializer()
            };

            ExampleOutput.WriteDim("Serializing identical object with each format:");
            ExampleOutput.WriteLine();

            foreach (var serializer in serializers)
            {
                var bytes = serializer.SerializeToBytes(person);
                var str = serializer.Serialize(person);
                var roundTrip = serializer.Deserialize<SamplePerson>(str)!;

                ExampleOutput.WriteInfo(serializer.Format.ToString(), $"{bytes.Length} bytes | ContentType: {serializer.ContentType}");
                ExampleOutput.WriteSuccess($"  Round-trip OK: {roundTrip.Name}, age {roundTrip.Age}");
            }

            ExampleOutput.WriteLine();
            ExampleOutput.WriteDim("Smaller byte size = more compact wire format.");
            ExampleOutput.WriteDim("Binary formats (MessagePack, Protobuf) are most compact.");
            ExampleOutput.WriteDim("JSON/XML are human-readable but larger.");
        }
    }
}
