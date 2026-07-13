using System;
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
    public class UserLogin : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserLogin>
        , IRelatedToUser
    {
        public Guid UserGuid { get; set; }

        /// <summary>
        /// Authentication provider: "local", "google", "apple", "microsoft", "facebook", "github", etc.
        /// </summary>
        public string Provider { get; set; } = null!;

        /// <summary>
        /// Unique key from the provider. For "local" this is the email/username.
        /// For OAuth this is the external user ID (sub claim).
        /// </summary>
        public string ProviderKey { get; set; } = null!;

        /// <summary>
        /// Password hash (only for Provider="local"). Stores Birko.Security Pbkdf2 hash.
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// JWT refresh token for this login method.
        /// </summary>
        public string? RefreshToken { get; set; }

        public DateTime? RefreshTokenExpiry { get; set; }

        /// <summary>
        /// Display name from the provider (e.g. "John via Google").
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Whether this login method has been verified (email confirmed for local, always true for OAuth).
        /// </summary>
        public bool IsVerified { get; set; }

        /// <summary>
        /// Last successful authentication using this method.
        /// </summary>
        public DateTime? LastUsedAt { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data?.Guid is Guid guid) // CR-M226
            {
                UserGuid = guid;
            }
        }

        /// <summary>
        /// CR-M227: the UserLogin view model carries no UserGuid — assign it via
        /// <c>LoadFrom(ViewModels.User)</c>, not this overload.
        /// </summary>
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
