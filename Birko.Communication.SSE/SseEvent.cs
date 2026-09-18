using System;
using System.Text;
using Birko.Serialization;
using Birko.Serialization.Json;

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
        /// Gets or sets an SSE comment. Serialized as `: {comment}` lines (which clients ignore) —
        /// NOT smuggled through <see cref="Data"/>, which would emit a `data: : ...` field instead of
        /// a real comment and corrupt the stream / defeat keep-alive (CR-M072).
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Returns the SSE formatted string representation of this event
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(Comment))
            {
                // A colon-prefixed line with no field name is an SSE comment.
                foreach (var line in Comment.Split('\n'))
                {
                    sb.Append($": {line.TrimEnd('\r')}").Append('\n');
                }
            }

            if (!string.IsNullOrEmpty(Id))
            {
                sb.Append($"id: {Id}").Append('\n');
            }

            if (!string.IsNullOrEmpty(Event))
            {
                sb.Append($"event: {Event}").Append('\n');
            }

            if (Retry.HasValue)
            {
                sb.Append($"retry: {Retry.Value}").Append('\n');
            }

            if (!string.IsNullOrEmpty(Data))
            {
                // Multi-line data handling - each line prefixed with "data: "
                var lines = Data.Split('\n');
                foreach (var line in lines)
                {
                    sb.Append($"data: {line.TrimEnd('\r')}").Append('\n');
                }
            }

            // Blank line to end the event
            sb.Append('\n');

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

        private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer();

        /// <summary>
        /// Creates a new SSE event with serialized data.
        /// </summary>
        public static SseEvent FromJson<T>(T data, string? @event = null, string? id = null, ISerializer? serializer = null)
        {
            var s = serializer ?? DefaultSerializer;
            return new SseEvent
            {
                Data = s.Serialize(data!),
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
                Comment = comment
            };
        }
    }
}
