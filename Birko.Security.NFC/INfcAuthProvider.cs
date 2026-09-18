using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.NFC
{
    /// <summary>
    /// Provides NFC-based authentication — maps tag UIDs to users and issues tokens.
    /// </summary>
    public interface INfcAuthProvider
    {
        /// <summary>
        /// Authenticate a user by their NFC tag UID.
        /// </summary>
        Task<NfcAuthResult> AuthenticateAsync(string tagUid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enroll (register) an NFC tag for a user.
        /// </summary>
        Task<NfcTagMapping> EnrollAsync(Guid userId, string tagUid, string? label = null, string? userName = null, string? email = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revoke (deactivate) an NFC tag.
        /// </summary>
        Task RevokeAsync(string tagUid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revoke all tags for a user.
        /// </summary>
        Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all active tag mappings for a user.
        /// </summary>
        Task<IReadOnlyList<NfcTagMapping>> GetUserTagsAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the tag mapping for a specific UID.
        /// </summary>
        Task<NfcTagMapping?> GetTagMappingAsync(string tagUid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a tag UID is already enrolled (active).
        /// </summary>
        Task<bool> IsEnrolledAsync(string tagUid, CancellationToken cancellationToken = default);
    }
}
