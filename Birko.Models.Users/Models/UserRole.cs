using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    /// <summary>
    /// Assigns a Role to a User, optionally scoped to a Tenant.
    /// TenantGuid = null means the role is global.
    /// TenantGuid set means the user has this role only within that tenant.
    /// </summary>
    public class UserRole : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserRole>
        , IRelatedToUser
        , IRelatedToRole
    {
        public Guid UserGuid { get; set; }
        public Guid RoleGuid { get; set; }

        /// <summary>
        /// Optional tenant scope. Null = global role assignment.
        /// </summary>
        public Guid? TenantGuid { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data != null)
            {
                UserGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.Role data)
        {
            if (data != null)
            {
                RoleGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.UserRole data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            TenantGuid = data.TenantGuid;
            GrantedAt = data.GrantedAt;
        }
    }
}
