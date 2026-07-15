using System;
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
            // CR-L282: explicit guard gives a meaningful ArgumentNullException naming 'body' rather than the
            // opaque one Encoding.UTF8.GetBytes(null) would throw.
            ArgumentNullException.ThrowIfNull(body);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Computes a SHA256 fingerprint of a QueueMessage's body.
        /// </summary>
        public static string Compute(QueueMessage message)
        {
            // CR-L282: guard the argument before dereferencing message.Body — a null message previously
            // threw NullReferenceException instead of a meaningful ArgumentNullException.
            ArgumentNullException.ThrowIfNull(message);
            return Compute(message.Body);
        }

        /// <summary>
        /// Computes a composite fingerprint from body + destination.
        /// Useful when the same payload sent to different destinations should be treated as distinct.
        /// </summary>
        public static string Compute(string destination, string body)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(body);
            var combined = destination + "\0" + body;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
