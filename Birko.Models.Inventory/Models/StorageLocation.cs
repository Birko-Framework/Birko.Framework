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
        , ICopyable<StorageLocation>
    {
        public string Title { get; set; } = null!;
        public int SortOrder { get; set; }
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = null!;
        public int Depth { get; set; }
        public Guid TenantGuid { get; set; }

        // CR-L309: typed CopyTo so cloning copies this model's own fields, consistent with
        // StockItem/StockItemVariant (the base CopyTo only handles AbstractLogModel fields).
        public virtual StorageLocation CopyTo(StorageLocation clone)
        {
            if (clone == null)
            {
                clone = new StorageLocation();
            }
            base.CopyTo(clone);
            clone.Title = Title;
            clone.SortOrder = SortOrder;
            clone.ParentGuid = ParentGuid;
            clone.Path = Path;
            clone.Depth = Depth;
            clone.TenantGuid = TenantGuid;
            return clone;
        }

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
