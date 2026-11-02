using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    /// <summary>
    /// One signed-in device: a row per refresh token (TASK-805). A login, a refresh and a tenant switch each act on their
    /// own row, so a user signed in on a phone and a PC keeps both, each in its own tenant. Issued, rotated and revoked by
    /// <see cref="Sessions.UserSessionService"/>.
    /// </summary>
    /// <remarks>
    /// The row holds the SHA-256 of the token, never the token. The view model carries no hash at all, so a session
    /// listed on a "where am I signed in" page cannot leak one.
    /// </remarks>
    public class UserSession : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserSession>
        , IRelatedToUser
    {
        public Guid UserGuid { get; set; }

        /// <summary>The login method the session was opened with, when the app says (local, google, …).</summary>
        public Guid? UserLoginGuid { get; set; }

        /// <summary>SHA-256 of the current refresh token (hex, uppercase).</summary>
        public string TokenHash { get; set; } = null!;

        /// <summary>
        /// SHA-256 of the token this row was last rotated from. Presenting that token again is a reuse: the session is
        /// revoked.
        /// </summary>
        public string? PreviousTokenHash { get; set; }

        /// <summary>The tenant the session is in; a refresh keeps it while the user may still use it.</summary>
        public Guid? TenantGuid { get; set; }

        /// <summary>A label for the device, typically the user agent.</summary>
        public string? DeviceLabel { get; set; }

        public DateTime LastUsedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        /// <summary>Why the session ended: <see cref="Sessions.SessionRevokeReasons"/> or the app's own word.</summary>
        public string? RevokedReason { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data?.Guid is Guid guid)
            {
                UserGuid = guid;
            }
        }

        /// <summary>
        /// The view model carries no user and no hashes, so neither is assigned here: a session cannot be re-pointed or
        /// handed a token through it.
        /// </summary>
        public virtual void LoadFrom(ViewModels.UserSession data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            TenantGuid = data.TenantGuid;
            DeviceLabel = data.DeviceLabel;
            LastUsedAt = data.LastUsedAt;
            ExpiresAt = data.ExpiresAt;
            RevokedAt = data.RevokedAt;
            RevokedReason = data.RevokedReason;
        }
    }
}
