using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// Records movement of stock (in/out/transfer between locations).
    /// </summary>
    public class StockMovement
        : AbstractLogModel
        , IDocumentLine
        , ILoadable<ViewModels.StockMovement>
    {
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? FromLocationGuid { get; set; }
        public Guid? ToLocationGuid { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Batch { get; set; }
        public DateTime MovementDate { get; set; }
        public Guid TenantGuid { get; set; }

        public virtual void LoadFrom(ViewModels.StockMovement data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            StockItemGuid = data.StockItemGuid;
            StockItemVariantGuid = data.StockItemVariantGuid;
            FromLocationGuid = data.FromLocationGuid;
            ToLocationGuid = data.ToLocationGuid;
            Quantity = data.Quantity;
            UnitPrice = data.UnitPrice;
            Batch = data.Batch;
            MovementDate = data.MovementDate;
            TenantGuid = data.TenantGuid;
        }
    }
}
