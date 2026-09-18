using System;
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
    public class Role : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.Role>
    {
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>
        /// System roles cannot be deleted or renamed (e.g. "admin", "owner").
        /// </summary>
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
