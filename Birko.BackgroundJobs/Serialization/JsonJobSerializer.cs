using System;
using System.Text.Json;

namespace Birko.BackgroundJobs.Serialization
{
    /// <summary>
    /// JSON-based job serializer using System.Text.Json.
    /// </summary>
    public class JsonJobSerializer : IJobSerializer
    {
        private readonly JsonSerializerOptions _options;

        public JsonJobSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public string Serialize(object input)
        {
            return JsonSerializer.Serialize(input, input.GetType(), _options);
        }

        public object? Deserialize(string data, Type type)
        {
            return JsonSerializer.Deserialize(data, type, _options);
        }
    }
}
