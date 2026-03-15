using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToRole : Birko.Data.Models.ILoadable<ViewModels.Role>
    {
        Guid RoleGuid { get; set; }
    }

    /// <summary>
    /// Named role definition. Permissions are app-specific and linked externally.
    /// </summary>
    [Table("Roles")]
    public class Role : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.Role>
    {
        [UniqueField]
        [PrecisionField(100)]
        public string Name { get; set; } = null!;

        [PrecisionField(500)]
        public string? Description { get; set; }

        /// <summary>
        /// System roles cannot be deleted or renamed (e.g. "admin", "owner").
        /// </summary>
        [NamedField("IsSystem")]
        public bool IsSystem { get; set; }

        public virtual void LoadFrom(ViewModels.Role data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Description = data.Description;
            IsSystem = data.IsSystem;
        }
    }
}
