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
        , IBatchable
        , ILoadable<ViewModels.StockMovement>
        , ICopyable<StockMovement>
    {
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? FromLocationGuid { get; set; }
        public Guid? ToLocationGuid { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        /// <inheritdoc />
        /// <remarks>
        /// Renamed from <c>Batch</c> in TASK-444 to match <see cref="IBatchable"/>. The old name came
        /// from the retired <c>Warehouse.AbstractItemRepository.Batch</c> and left the framework using
        /// two words for one concept in a single namespace. Breaking, and taken deliberately while
        /// nothing in the framework or in Symbio read it.
        /// </remarks>
        public string? BatchNumber { get; set; }

        /// <inheritdoc />
        /// <remarks>
        /// A receipt is where a batch's expiry enters the system, so the movement is the natural place
        /// to capture it — without it, <see cref="StockBalance.ExpiryDate"/> would have no source in
        /// the model.
        /// </remarks>
        public DateTime? ExpiryDate { get; set; }

        public DateTime MovementDate { get; set; }
        public Guid TenantGuid { get; set; }

        // CR-L309: typed CopyTo so cloning copies this model's own fields (base handles only
        // AbstractLogModel fields), consistent with StockItem/StockItemVariant.
        public virtual StockMovement CopyTo(StockMovement clone)
        {
            if (clone == null)
            {
                clone = new StockMovement();
            }
            base.CopyTo(clone);
            clone.StockItemGuid = StockItemGuid;
            clone.StockItemVariantGuid = StockItemVariantGuid;
            clone.FromLocationGuid = FromLocationGuid;
            clone.ToLocationGuid = ToLocationGuid;
            clone.Quantity = Quantity;
            clone.UnitPrice = UnitPrice;
            clone.BatchNumber = BatchNumber;
            clone.ExpiryDate = ExpiryDate;
            clone.MovementDate = MovementDate;
            clone.TenantGuid = TenantGuid;
            return clone;
        }

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
            BatchNumber = data.BatchNumber;
            ExpiryDate = data.ExpiryDate;
            MovementDate = data.MovementDate;
            TenantGuid = data.TenantGuid;
        }
    }
}
