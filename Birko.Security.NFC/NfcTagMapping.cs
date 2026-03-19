using System;

namespace Birko.Security.NFC
{
    /// <summary>
    /// Maps an NFC tag UID to a user identity.
    /// Stored in any Birko.Data store for persistence.
    /// </summary>
    public class NfcTagMapping
    {
        /// <summary>
        /// Unique identifier for this mapping record.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// NFC tag UID (hex string, e.g., "04A1B2C3D4E5F6").
        /// </summary>
        public string TagUid { get; set; } = string.Empty;

        /// <summary>
        /// User ID this tag is mapped to.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// User display name (denormalized for convenience).
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// User email (denormalized for convenience).
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Whether this mapping is currently active.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When this tag was enrolled/registered.
        /// </summary>
        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this mapping was last used for authentication.
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        /// <summary>
        /// Optional label for the card (e.g., "Office badge", "Backup card").
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Optional expiration date. Null means no expiration.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Whether this mapping has expired.
        /// </summary>
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    }
}
