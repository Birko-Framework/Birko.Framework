using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    /// <summary>
    /// Personal information for a User (1:1). Separated from User for privacy/GDPR
    /// (easier export/delete) and lazy-loading (most API calls don't need profile data).
    /// </summary>
    public class UserProfile : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserProfile>
        , IRelatedToUser
    {
        public Guid UserGuid { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        /// <summary>
        /// Preferred display name. Falls back to FirstName + LastName if null.
        /// </summary>
        public string? DisplayName { get; set; }

        public string? Phone { get; set; }

        /// <summary>
        /// URL or storage path to the user's avatar image.
        /// </summary>
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// Preferred locale/language code (e.g. "sk", "en", "cs").
        /// </summary>
        public string? Locale { get; set; }

        /// <summary>
        /// IANA time zone identifier (e.g. "Europe/Bratislava").
        /// </summary>
        public string? TimeZone { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? Bio { get; set; }

        /// <summary>
        /// Computed display name: explicit DisplayName, or "FirstName LastName", or empty.
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(DisplayName))
                return DisplayName;
            if (!string.IsNullOrEmpty(FirstName) || !string.IsNullOrEmpty(LastName))
                return $"{FirstName} {LastName}".Trim();
            return string.Empty;
        }

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data?.Guid is Guid guid) // CR-M226
            {
                UserGuid = guid;
            }
        }

        /// <summary>
        /// CR-M227: the UserProfile view model carries no UserGuid — assign it via
        /// <c>LoadFrom(ViewModels.User)</c>, not this overload.
        /// </summary>
        public virtual void LoadFrom(ViewModels.UserProfile data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            FirstName = data.FirstName;
            LastName = data.LastName;
            DisplayName = data.DisplayName;
            Phone = data.Phone;
            AvatarUrl = data.AvatarUrl;
            Locale = data.Locale;
            TimeZone = data.TimeZone;
            DateOfBirth = data.DateOfBirth;
            Bio = data.Bio;
        }
    }
}
