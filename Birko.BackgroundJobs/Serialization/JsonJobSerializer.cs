using System;
using System.Text.Json;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.BackgroundJobs.Serialization
{
    /// <summary>
    /// JSON-based job serializer using System.Text.Json.
    /// </summary>
    public class JsonJobSerializer : IJobSerializer
    {
        private readonly ISerializer _serializer;

        public JsonJobSerializer(JsonSerializerOptions? options = null)
        {
            _serializer = new SystemJsonSerializer(options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        /// <summary>
        /// Creates a job serializer backed by a custom ISerializer.
        /// </summary>
        public JsonJobSerializer(ISerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public string Serialize(object input)
        {
            return _serializer.Serialize(input);
        }

        public object? Deserialize(string data, Type type)
        {
            return _serializer.Deserialize(data, type);
        }
    }
}
