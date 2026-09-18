using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Birko.Data.Models;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.CosmosDB.Serialization
{
    /// <summary>
    /// Cosmos serializer that guarantees every <see cref="AbstractModel"/> document carries a string
    /// <c>id</c> equal to its <c>Guid</c>. Cosmos requires an <c>id</c> field on every document, and
    /// with the container's partition key path <c>/id</c> the partition-key value must equal that
    /// <c>id</c>; but <see cref="AbstractModel"/> only exposes <c>Guid</c> (serialized PascalCase),
    /// never <c>id</c> — so point reads/writes keyed by <c>guid.ToString()</c> could never locate the
    /// document (CR-C04). This injects <c>id</c> on write; the model's own <c>Guid</c> is still emitted
    /// (for round-trip) and the extra <c>id</c> is simply ignored on read. Non-model types (query
    /// responses, account settings, etc.) serialize normally.
    /// </summary>
    public sealed class CosmosGuidIdSerializer : CosmosSerializer
    {
        private readonly JsonSerializerOptions _options;

        public CosmosGuidIdSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions();
        }

        public override T FromStream<T>(Stream stream)
        {
            using (stream)
            {
                // The Cosmos SDK sometimes reads the payload as a raw Stream.
                if (typeof(Stream).IsAssignableFrom(typeof(T)))
                {
                    return (T)(object)stream;
                }

                if (stream.CanSeek && stream.Length == 0)
                {
                    return default!;
                }

                using var doc = JsonDocument.Parse(stream);
                return doc.RootElement.Deserialize<T>(_options)!;
            }
        }

        public override Stream ToStream<T>(T input)
        {
            // The Cosmos SDK sometimes passes a Stream through directly.
            if (input is Stream passthrough)
            {
                return passthrough;
            }

            var node = JsonSerializer.SerializeToNode(input, input?.GetType() ?? typeof(T), _options);

            // Inject the Cosmos 'id' from the model's Guid so point operations by guid.ToString() resolve.
            if (node is JsonObject obj && input is AbstractModel model && model.Guid.HasValue && obj["id"] is null)
            {
                obj["id"] = model.Guid.Value.ToString();
            }

            var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                if (node is null)
                {
                    JsonSerializer.Serialize(writer, input, _options);
                }
                else
                {
                    node.WriteTo(writer, _options);
                }
            }
            ms.Position = 0;
            return ms;
        }
    }
}
