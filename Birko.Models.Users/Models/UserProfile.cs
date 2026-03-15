using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    /// <summary>
    /// Personal information for a User (1:1). Separated from User for privacy/GDPR
    /// (easier export/delete) and lazy-loading (most API calls don't need profile data).
    /// </summary>
    [Table("UserProfiles")]
    public class UserProfile : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserProfile>
        , IRelatedToUser
    {
        [UniqueField]
        public Guid UserGuid { get; set; }

        [PrecisionField(100)]
        public string? FirstName { get; set; }

        [PrecisionField(100)]
        public string? LastName { get; set; }

        /// <summary>
        /// Preferred display name. Falls back to FirstName + LastName if null.
        /// </summary>
        [PrecisionField(200)]
        public string? DisplayName { get; set; }

        [PrecisionField(20)]
        public string? Phone { get; set; }

        /// <summary>
        /// URL or storage path to the user's avatar image.
        /// </summary>
        [PrecisionField(500)]
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// Preferred locale/language code (e.g. "sk", "en", "cs").
        /// </summary>
        [PrecisionField(10)]
        public string? Locale { get; set; }

        /// <summary>
        /// IANA time zone identifier (e.g. "Europe/Bratislava").
        /// </summary>
        [PrecisionField(50)]
        public string? TimeZone { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [PrecisionField(500)]
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
            if (data != null)
            {
                UserGuid = data.Guid!.Value;
            }
        }

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
