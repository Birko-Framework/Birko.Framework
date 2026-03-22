using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public class UserTenant : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserTenant>
        , IRelatedToUser
        , IRelatedToTenant
    {
        public Guid UserGuid { get; set; }
        public Guid TenantGuid { get; set; }

        public bool IsOwner { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data != null)
            {
                UserGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.UserTenant data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            IsOwner = data.IsOwner;
            JoinedAt = data.JoinedAt;
        }

        public virtual void LoadFrom(ViewModels.Tenant data)
        {
            if (data != null)
            {
                TenantGuid = data.Guid!.Value;
            }
        }
    }
}
