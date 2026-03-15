using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToUserLogin : Birko.Data.Models.ILoadable<ViewModels.UserLogin>
    {
        Guid UserLoginGuid { get; set; }
    }

    /// <summary>
    /// Authentication method linked to a User. One user can have multiple logins
    /// (e.g. local password + Google OAuth + Apple Sign-In).
    /// </summary>
    [Table("UserLogins")]
    public class UserLogin : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserLogin>
        , IRelatedToUser
    {
        public Guid UserGuid { get; set; }

        /// <summary>
        /// Authentication provider: "local", "google", "apple", "microsoft", "facebook", "github", etc.
        /// </summary>
        [PrecisionField(50)]
        public string Provider { get; set; } = null!;

        /// <summary>
        /// Unique key from the provider. For "local" this is the email/username.
        /// For OAuth this is the external user ID (sub claim).
        /// </summary>
        [PrecisionField(256)]
        public string ProviderKey { get; set; } = null!;

        /// <summary>
        /// Password hash (only for Provider="local"). Stores Birko.Security Pbkdf2 hash.
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// JWT refresh token for this login method.
        /// </summary>
        [PrecisionField(512)]
        public string? RefreshToken { get; set; }

        public DateTime? RefreshTokenExpiry { get; set; }

        /// <summary>
        /// Display name from the provider (e.g. "John via Google").
        /// </summary>
        [PrecisionField(256)]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Whether this login method has been verified (email confirmed for local, always true for OAuth).
        /// </summary>
        [NamedField("IsVerified")]
        public bool IsVerified { get; set; }

        /// <summary>
        /// Last successful authentication using this method.
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data != null)
            {
                UserGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.UserLogin data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Provider = data.Provider;
            ProviderKey = data.ProviderKey;
            PasswordHash = data.PasswordHash;
            RefreshToken = data.RefreshToken;
            RefreshTokenExpiry = data.RefreshTokenExpiry;
            DisplayName = data.DisplayName;
            IsVerified = data.IsVerified;
            LastUsedAt = data.LastUsedAt;
        }
    }
}
