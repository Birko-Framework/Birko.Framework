using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// Line item within an inventory document.
    /// Clean replacement for Warehouse.WareHouseDocumentItem — pricing uses value objects.
    /// </summary>
    public class InventoryDocumentLine
        : AbstractLogModel
        , IDocumentLine
        , ILoadable<ViewModels.InventoryDocumentLine>
    {
        public Guid InventoryDocumentGuid { get; set; }
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? StorageLocationGuid { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? Batch { get; set; }
        public decimal Quantity { get; set; }
        public string? MeasureUnit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? UnitPriceVAT { get; set; }
        public decimal? VAT { get; set; }
        public decimal? TotalPrice { get; set; }
        public decimal? TotalPriceVAT { get; set; }

        public virtual void LoadFrom(ViewModels.InventoryDocumentLine data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            InventoryDocumentGuid = data.InventoryDocumentGuid;
            StockItemGuid = data.StockItemGuid;
            StockItemVariantGuid = data.StockItemVariantGuid;
            StorageLocationGuid = data.StorageLocationGuid;
            Name = data.Name;
            Description = data.Description;
            Batch = data.Batch;
            Quantity = data.Quantity;
            MeasureUnit = data.MeasureUnit;
            UnitPrice = data.UnitPrice;
            UnitPriceVAT = data.UnitPriceVAT;
            VAT = data.VAT;
            TotalPrice = data.TotalPrice;
            TotalPriceVAT = data.TotalPriceVAT;
        }
    }
}
