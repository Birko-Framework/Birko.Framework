using System;
using System.Text;
using System.Text.Json;

namespace Birko.Communication.SSE
{
    /// <summary>
    /// Represents a Server-Sent Event according to the W3C specification
    /// </summary>
    public class SseEvent
    {
        /// <summary>
        /// Gets or sets the unique event identifier
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the event type/name
        /// </summary>
        public string? Event { get; set; }

        /// <summary>
        /// Gets or sets the event data payload
        /// </summary>
        public string? Data { get; set; }

        /// <summary>
        /// Gets or sets the reconnection delay in milliseconds
        /// </summary>
        public int? Retry { get; set; }

        /// <summary>
        /// Returns the SSE formatted string representation of this event
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(Id))
            {
                sb.AppendLine($"id: {Id}");
            }

            if (!string.IsNullOrEmpty(Event))
            {
                sb.AppendLine($"event: {Event}");
            }

            if (Retry.HasValue)
            {
                sb.AppendLine($"retry: {Retry.Value}");
            }

            if (!string.IsNullOrEmpty(Data))
            {
                // Multi-line data handling - each line prefixed with "data: "
                var lines = Data.Split('\n');
                foreach (var line in lines)
                {
                    sb.AppendLine($"data: {line.TrimEnd('\r')}");
                }
            }

            // Blank line to end the event
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Creates a new SSE event with the specified data
        /// </summary>
        public static SseEvent Create(string? data, string? @event = null, string? id = null, int? retry = null)
        {
            return new SseEvent
            {
                Data = data,
                Event = @event,
                Id = id,
                Retry = retry
            };
        }

        /// <summary>
        /// Creates a new SSE event with JSON serialized data
        /// </summary>
        public static SseEvent FromJson<T>(T data, string? @event = null, string? id = null)
        {
            return new SseEvent
            {
                Data = JsonSerializer.Serialize(data),
                Event = @event,
                Id = id ?? Guid.NewGuid().ToString()
            };
        }

        /// <summary>
        /// Creates a comment event (lines starting with ':' are ignored by clients)
        /// Useful for keeping connections alive without sending actual events
        /// </summary>
        public static SseEvent CreateComment(string comment)
        {
            return new SseEvent
            {
                Data = $": {comment}"
            };
        }
    }
}
