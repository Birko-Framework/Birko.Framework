using System;
using System.Text.Json;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.MessageQueue.Serialization
{
    /// <summary>
    /// JSON message serializer using System.Text.Json.
    /// </summary>
    public class JsonMessageSerializer : IMessageSerializer
    {
        private readonly ISerializer _serializer;

        public JsonMessageSerializer(JsonSerializerOptions? options = null)
        {
            _serializer = new SystemJsonSerializer(options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        /// <summary>
        /// Creates a message serializer backed by a custom ISerializer.
        /// </summary>
        public JsonMessageSerializer(ISerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public string ContentType => _serializer.ContentType;

        public string Serialize(object payload)
        {
            return _serializer.Serialize(payload);
        }

        public object? Deserialize(string data, Type type)
        {
            return _serializer.Deserialize(data, type);
        }

        public T? Deserialize<T>(string data) where T : class
        {
            return _serializer.Deserialize<T>(data);
        }
    }
}
