using System.Security.Cryptography;
using System.Text;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Generates content-based fingerprints for message deduplication.
    /// Uses SHA256 to create a deterministic hash of the message body.
    /// </summary>
    public static class MessageFingerprint
    {
        /// <summary>
        /// Computes a SHA256 fingerprint of the message body.
        /// Two messages with identical bodies produce the same fingerprint.
        /// </summary>
        public static string Compute(string body)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Computes a SHA256 fingerprint of a QueueMessage's body.
        /// </summary>
        public static string Compute(QueueMessage message)
        {
            return Compute(message.Body);
        }

        /// <summary>
        /// Computes a composite fingerprint from body + destination.
        /// Useful when the same payload sent to different destinations should be treated as distinct.
        /// </summary>
        public static string Compute(string destination, string body)
        {
            var combined = destination + "\0" + body;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
