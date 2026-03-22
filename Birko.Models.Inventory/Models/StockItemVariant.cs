using System;
using Birko.Data.Models;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// Variant of a stock item (size, color, configuration).
    /// Clean replacement for Warehouse.ItemVariant.
    /// </summary>
    public class StockItemVariant
        : AbstractLogModel
        , ILoadable<ViewModels.StockItemVariant>
        , ICopyable<StockItemVariant>
    {
        public string Name { get; set; } = null!;
        public Guid? StockItemGuid { get; set; }

        public virtual StockItemVariant CopyTo(StockItemVariant clone)
        {
            if (clone == null)
            {
                clone = new StockItemVariant();
            }
            base.CopyTo(clone);
            clone.Name = Name;
            clone.StockItemGuid = StockItemGuid;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.StockItemVariant data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Name = data.Name;
            StockItemGuid = data.StockItemGuid;
        }
    }
}
