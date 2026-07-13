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
            if (data?.Guid is Guid guid) // CR-M226: guard the nullable Guid, not just the object
            {
                UserGuid = guid;
            }
        }

        public virtual void LoadFrom(ViewModels.Role data)
        {
            if (data?.Guid is Guid guid) // CR-M226
            {
                RoleGuid = guid;
            }
        }

        /// <summary>
        /// CR-M227: the UserRole view model carries no UserGuid/RoleGuid, so those foreign keys are NOT
        /// restored by this overload — assign them via <c>LoadFrom(ViewModels.User)</c> /
        /// <c>LoadFrom(ViewModels.Role)</c>. A single VM→model load is intentionally not a full FK round-trip.
        /// </summary>
        public virtual void LoadFrom(ViewModels.UserRole data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            TenantGuid = data.TenantGuid;
            GrantedAt = data.GrantedAt;
        }
    }
}
