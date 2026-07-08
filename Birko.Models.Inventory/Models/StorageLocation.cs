using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// Physical storage location in a warehouse (shelf, bin, zone).
    /// Clean replacement for Warehouse.Repository — renamed to avoid
    /// clash with data access pattern.
    /// </summary>
    public class StorageLocation
        : AbstractLogModel
        , IHierarchical
        , ILoadable<ViewModels.StorageLocation>
    {
        public string Title { get; set; } = null!;
        public int SortOrder { get; set; }
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = null!;
        public int Depth { get; set; }
        public Guid TenantGuid { get; set; }

        public virtual void LoadFrom(ViewModels.StorageLocation data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            SortOrder = data.SortOrder;
            ParentGuid = data.ParentGuid;
            Path = data.Path;
            // Depth is the coupled half of the materialized-path scheme (Path/Depth are set together
            // by HierarchyHelper.ComputePath); round-trip it too, or Path stays but Depth zeroes out.
            Depth = data.Depth;
            TenantGuid = data.TenantGuid;
        }
    }
}
