using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToUser : Birko.Data.Models.ILoadable<ViewModels.User>
    {
        Guid UserGuid { get; set; }
    }

    [Table("Users")]
    public class User : Birko.Data.Models.AbstractDatabaseLogModel, Birko.Data.Models.ILoadable<ViewModels.User>
    {
        public const string UserRolesSeparator = ",";

        [UniqueField]
        [PrecisionField(256)]
        public string UserName { get; set; } = null!;

        [UniqueField]
        [PrecisionField(256)]
        public string? Email { get; set; }

        public string? Roles { get; set; }

        [NamedField("IsActive")]
        public bool IsActive { get; set; } = true;

        public DateTime? LastLoginAt { get; set; }

        [NamedField("EmailVerified")]
        public bool EmailVerified { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            UserName = data.UserName;
            Email = data.Email;
            Roles = (data.Roles != null && data.Roles.Any(x => !string.IsNullOrEmpty(x)))
                ? string.Join(UserRolesSeparator, data.Roles.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
                : null;
            IsActive = data.IsActive;
            LastLoginAt = data.LastLoginAt;
            EmailVerified = data.EmailVerified;
        }
    }
}
