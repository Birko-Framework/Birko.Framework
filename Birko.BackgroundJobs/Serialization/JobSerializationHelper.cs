using System.Collections.Generic;
using Birko.Serialization.Json;

namespace Birko.BackgroundJobs.Models
{
    /// <summary>
    /// Shared JSON serialization helper for background job models.
    /// Uses Birko.Serialization for consistent JSON handling across all job storage platforms.
    /// </summary>
    internal static class JobSerializationHelper
    {
        private static readonly SystemJsonSerializer _serializer = new(new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        /// <summary>
        /// Serializes metadata dictionary to JSON string.
        /// </summary>
        public static string? SerializeMetadata(IDictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return null;
            return _serializer.Serialize(metadata);
        }

        /// <summary>
        /// Deserializes JSON string to metadata dictionary.
        /// </summary>
        public static IDictionary<string, string>? DeserializeMetadata(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return _serializer.Deserialize<IDictionary<string, string>>(json);
        }
    }
}
