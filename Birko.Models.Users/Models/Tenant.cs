using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToTenant : Birko.Data.Models.ILoadable<ViewModels.Tenant>
    {
        Guid TenantGuid { get; set; }
    }

    public class Tenant
        : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.Tenant>
        , IDefault
    {
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public bool Default { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public virtual void LoadFrom(ViewModels.Tenant data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Description = data.Description;
            Default = data.Default;
            IsActive = data.IsActive;
        }
    }
}
