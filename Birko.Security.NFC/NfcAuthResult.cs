using System;
using System.Collections.Generic;

namespace Birko.Security.NFC
{
    /// <summary>
    /// Result of NFC tag authentication.
    /// </summary>
    public class NfcAuthResult
    {
        /// <summary>
        /// Whether the authentication was successful.
        /// </summary>
        public bool IsAuthenticated { get; init; }

        /// <summary>
        /// Authenticated user ID (null if authentication failed).
        /// </summary>
        public Guid? UserId { get; init; }

        /// <summary>
        /// User display name (optional).
        /// </summary>
        public string? UserName { get; init; }

        /// <summary>
        /// User email (optional).
        /// </summary>
        public string? Email { get; init; }

        /// <summary>
        /// JWT token issued for the authenticated user (null if authentication failed).
        /// </summary>
        public TokenResult? Token { get; init; }

        /// <summary>
        /// Error message if authentication failed.
        /// </summary>
        public string? Error { get; init; }

        /// <summary>
        /// Additional claims or metadata about the authenticated user.
        /// </summary>
        public IDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();

        /// <summary>
        /// The tag UID that was used for authentication.
        /// </summary>
        public string TagUid { get; init; } = string.Empty;

        /// <summary>
        /// Timestamp of the authentication attempt.
        /// </summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        public static NfcAuthResult Success(Guid userId, string tagUid, TokenResult? token = null, string? userName = null, string? email = null, IDictionary<string, string>? claims = null)
        {
            return new NfcAuthResult
            {
                IsAuthenticated = true,
                UserId = userId,
                TagUid = tagUid,
                Token = token,
                UserName = userName,
                Email = email,
                Claims = claims ?? new Dictionary<string, string>()
            };
        }

        public static NfcAuthResult Failure(string tagUid, string error)
        {
            return new NfcAuthResult
            {
                IsAuthenticated = false,
                TagUid = tagUid,
                Error = error
            };
        }
    }
}
