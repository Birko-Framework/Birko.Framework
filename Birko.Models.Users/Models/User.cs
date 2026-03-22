using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToUser : Birko.Data.Models.ILoadable<ViewModels.User>
    {
        Guid UserGuid { get; set; }
    }

    public class User : Birko.Data.Models.AbstractLogModel, Birko.Data.Models.ILoadable<ViewModels.User>
    {
        public string UserName { get; set; } = null!;

        public string? Email { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastLoginAt { get; set; }

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
