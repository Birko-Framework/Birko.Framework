using System;
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
        [UniqueField]
        [PrecisionField(256)]
        public string UserName { get; set; } = null!;

        [UniqueField]
        [PrecisionField(256)]
        public string? Email { get; set; }

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
            IsActive = data.IsActive;
            LastLoginAt = data.LastLoginAt;
            EmailVerified = data.EmailVerified;
        }
    }
}
