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
        , IBatchable
        , ILoadable<ViewModels.InventoryDocumentLine>
        , ICopyable<InventoryDocumentLine>
    {
        public Guid InventoryDocumentGuid { get; set; }
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? StorageLocationGuid { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        /// <inheritdoc />
        /// <remarks>Renamed from <c>Batch</c> in TASK-444 to match <see cref="IBatchable"/>, alongside
        /// <see cref="StockMovement"/> and the new <see cref="StockBalance"/> — one word for one concept
        /// across the namespace.</remarks>
        public string? BatchNumber { get; set; }

        /// <inheritdoc />
        public DateTime? ExpiryDate { get; set; }

        public decimal Quantity { get; set; }
        public Guid? MeasureUnitGuid { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? UnitPriceVAT { get; set; }
        public decimal? VAT { get; set; }
        public decimal? TotalPrice { get; set; }
        public decimal? TotalPriceVAT { get; set; }

        // CR-L309: typed CopyTo so cloning copies this line's own fields (base handles only
        // AbstractLogModel fields), consistent with StockItem/StockItemVariant.
        public virtual InventoryDocumentLine CopyTo(InventoryDocumentLine clone)
        {
            if (clone == null)
            {
                clone = new InventoryDocumentLine();
            }
            base.CopyTo(clone);
            clone.InventoryDocumentGuid = InventoryDocumentGuid;
            clone.StockItemGuid = StockItemGuid;
            clone.StockItemVariantGuid = StockItemVariantGuid;
            clone.StorageLocationGuid = StorageLocationGuid;
            clone.Name = Name;
            clone.Description = Description;
            clone.BatchNumber = BatchNumber;
            clone.ExpiryDate = ExpiryDate;
            clone.Quantity = Quantity;
            clone.MeasureUnitGuid = MeasureUnitGuid;
            clone.UnitPrice = UnitPrice;
            clone.UnitPriceVAT = UnitPriceVAT;
            clone.VAT = VAT;
            clone.TotalPrice = TotalPrice;
            clone.TotalPriceVAT = TotalPriceVAT;
            return clone;
        }

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
            BatchNumber = data.BatchNumber;
            ExpiryDate = data.ExpiryDate;
            Quantity = data.Quantity;
            MeasureUnitGuid = data.MeasureUnitGuid;
            UnitPrice = data.UnitPrice;
            UnitPriceVAT = data.UnitPriceVAT;
            VAT = data.VAT;
            TotalPrice = data.TotalPrice;
            TotalPriceVAT = data.TotalPriceVAT;
        }
    }
}
