using System;

namespace Birko.Models.Inventory.ViewModels
{
    public class StockBalance : Data.ViewModels.LogViewModel
    {
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? StorageLocationGuid { get; set; }
        public decimal Quantity { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public Guid TenantGuid { get; set; }
    }
}
