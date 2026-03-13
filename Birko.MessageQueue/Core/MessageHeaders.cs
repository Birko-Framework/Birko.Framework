using System.Collections.Generic;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Message metadata headers. Provides standard headers and custom key-value pairs.
    /// </summary>
    public class MessageHeaders
    {
        /// <summary>
        /// Correlation ID for tracking related messages across services.
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Destination for reply messages (request-reply pattern).
        /// </summary>
        public string? ReplyTo { get; set; }

        /// <summary>
        /// Content type of the body (e.g., "application/json").
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// Message group/session ID for ordered delivery within a group.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Custom headers.
        /// </summary>
        public Dictionary<string, string> Custom { get; set; } = new();
    }
}
