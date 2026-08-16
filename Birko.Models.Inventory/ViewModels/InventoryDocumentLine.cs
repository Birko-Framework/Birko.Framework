using System;

namespace Birko.Models.Inventory.ViewModels
{
    public class InventoryDocumentLine : Data.ViewModels.LogViewModel
    {
        public Guid InventoryDocumentGuid { get; set; }
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? StorageLocationGuid { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal Quantity { get; set; }
        public Guid? MeasureUnitGuid { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? UnitPriceVAT { get; set; }
        public decimal? VAT { get; set; }
        public decimal? TotalPrice { get; set; }
        public decimal? TotalPriceVAT { get; set; }
    }
}
