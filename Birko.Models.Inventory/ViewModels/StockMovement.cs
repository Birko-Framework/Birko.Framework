using System;

namespace Birko.Models.Inventory.ViewModels
{
    public class StockMovement : Data.ViewModels.LogViewModel
    {
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? FromLocationGuid { get; set; }
        public Guid? ToLocationGuid { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime MovementDate { get; set; }
        public Guid TenantGuid { get; set; }
    }
}
