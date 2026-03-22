using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// Inventory item tracked in stock. Clean replacement for Warehouse.Item.
    /// </summary>
    public class StockItem
        : AbstractLogModel
        , ICatalogItem
        , ICategorizeable
        , ILoadable<ViewModels.StockItem>
        , ICopyable<StockItem>
    {
        public string Code { get; set; } = null!;
        public string BarCode { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string ShortName { get; set; } = null!;
        public string Description { get; set; } = null!;
        public Guid? CategoryGuid { get; set; }
        public Guid? MeasureUnitGuid { get; set; }
        public string Type { get; set; } = null!;
        public Guid TenantGuid { get; set; }

        public virtual StockItem CopyTo(StockItem clone)
        {
            if (clone == null)
            {
                clone = new StockItem();
            }
            base.CopyTo(clone);
            clone.Code = Code;
            clone.BarCode = BarCode;
            clone.Name = Name;
            clone.ShortName = ShortName;
            clone.Description = Description;
            clone.CategoryGuid = CategoryGuid;
            clone.MeasureUnitGuid = MeasureUnitGuid;
            clone.Type = Type;
            clone.TenantGuid = TenantGuid;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.StockItem data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Code = data.Code;
            BarCode = data.BarCode;
            Name = data.Name;
            ShortName = data.ShortName;
            Description = data.Description;
            CategoryGuid = data.CategoryGuid;
            MeasureUnitGuid = data.MeasureUnitGuid;
            Type = data.Type;
            TenantGuid = data.TenantGuid;
        }
    }
}
