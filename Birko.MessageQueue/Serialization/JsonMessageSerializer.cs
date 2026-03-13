using System;
using System.Text.Json;

namespace Birko.MessageQueue.Serialization
{
    /// <summary>
    /// JSON message serializer using System.Text.Json.
    /// </summary>
    public class JsonMessageSerializer : IMessageSerializer
    {
        private readonly JsonSerializerOptions _options;

        public JsonMessageSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public string ContentType => "application/json";

        public string Serialize(object payload)
        {
            return JsonSerializer.Serialize(payload, payload.GetType(), _options);
        }

        public object? Deserialize(string data, Type type)
        {
            return JsonSerializer.Deserialize(data, type, _options);
        }

        public T? Deserialize<T>(string data) where T : class
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
    }
}
