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
        public static Dictionary<string, string>? DeserializeMetadata(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var result = _serializer.Deserialize<IDictionary<string, string>>(json);
            // TASK-158 (SH-M004): JSON "null" deserializes to null, and the copy constructor threw on it inside every
            // backend's ToDescriptor, so one such row broke Get, Dequeue and GetByStatus.
            if (result == null) return new Dictionary<string, string>();
            return result as Dictionary<string, string> ?? new Dictionary<string, string>(result);
        }
    }
}
