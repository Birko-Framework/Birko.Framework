using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    /// <summary>
    /// Links a Role to a permission code string.
    /// Permission codes are defined as constants in each module (e.g. "iot:device:register").
    /// No Permission entity needed — codes are just strings.
    /// </summary>
    [Table("RolePermissions")]
    public class RolePermission : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.RolePermission>
        , IRelatedToRole
    {
        public Guid RoleGuid { get; set; }

        /// <summary>
        /// Permission code string (e.g. "iot:device:register", "building:space:create").
        /// Convention: {module}:{entity}:{action}
        /// </summary>
        [PrecisionField(200)]
        public string PermissionCode { get; set; } = null!;

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        public virtual void LoadFrom(ViewModels.Role data)
        {
            if (data != null)
            {
                RoleGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.RolePermission data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            PermissionCode = data.PermissionCode;
            GrantedAt = data.GrantedAt;
        }
    }
}
