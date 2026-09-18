using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    /// <summary>
    /// How much of an item is at a location right now — the stock <b>balance</b>, as opposed to
    /// <see cref="StockMovement"/>, which records a change to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The natural key is item × variant × location × batch, scoped by tenant. One row is one bucket of
    /// stock; <see cref="Quantity"/> is its current size.
    /// </para>
    /// <para>
    /// <b>Why this exists (TASK-444).</b> The migration from the retired <c>Birko.Models.Warehouse</c>
    /// recorded <c>AbstractItemRepository → StockMovement</c> as a one-to-one rename. Read back from that
    /// project's surviving source (in <c>FisData.Stock.Core</c>'s git history),
    /// <c>AbstractItemRepository</c> was an <b>abstract coordinate base</b> — item, variant, repository,
    /// agenda, batch, and no quantity or date at all — with three concrete descendants:
    /// <c>ItemRepository</c> (+ <c>Amount</c>) was the balance, <c>ItemRepositoryMovement</c>
    /// (+ amount, prices, document, date) was the ledger, and <c>ItemRepositoryInventory</c>
    /// (+ start/add/remove/end amounts) was a period snapshot. Collapsing that hierarchy onto one
    /// concrete movement class left the domain with no way to express a balance, and two consumers
    /// independently re-added one — FisData by bolting <c>StorageLocationGuid</c> and <c>Amount</c> onto
    /// <see cref="StockMovement"/>, shadowing its movement-shaped fields.
    /// </para>
    /// <para>
    /// <b>No cost field, deliberately.</b> A balance's value depends on a costing <i>policy</i> — FIFO,
    /// LIFO, weighted average all produce different numbers from the same movements — so it is derived,
    /// not stored state, and putting it here would force one policy on every consumer. The original
    /// design agreed: <c>ItemRepository</c> carried <c>Amount</c> alone while every price lived on
    /// <c>ItemRepositoryMovement</c>. A consumer that wants a valuation keeps it beside its own pricing
    /// configuration, as Symbio does with <c>AverageCost</c> under a per-warehouse <c>PricingMethod</c>.
    /// Reservations, min/max and reorder points are the same kind of thing and are likewise absent.
    /// </para>
    /// </remarks>
    public class StockBalance
        : AbstractLogModel
        , IBatchable
        , ILoadable<ViewModels.StockBalance>
        , ICopyable<StockBalance>
    {
        public Guid? StockItemGuid { get; set; }
        public Guid? StockItemVariantGuid { get; set; }
        public Guid? StorageLocationGuid { get; set; }

        /// <summary>
        /// Current quantity in this bucket. Signed on purpose: a negative balance is a real state that
        /// consumers need to be able to represent (and detect) rather than one the model forbids.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <inheritdoc />
        public string? BatchNumber { get; set; }

        /// <inheritdoc />
        public DateTime? ExpiryDate { get; set; }

        public Guid TenantGuid { get; set; }

        // CR-L309: typed CopyTo so cloning copies this model's own fields (base handles only
        // AbstractLogModel fields), consistent with StockItem/StockItemVariant/StockMovement.
        public virtual StockBalance CopyTo(StockBalance clone)
        {
            if (clone == null)
            {
                clone = new StockBalance();
            }
            base.CopyTo(clone);
            clone.StockItemGuid = StockItemGuid;
            clone.StockItemVariantGuid = StockItemVariantGuid;
            clone.StorageLocationGuid = StorageLocationGuid;
            clone.Quantity = Quantity;
            clone.BatchNumber = BatchNumber;
            clone.ExpiryDate = ExpiryDate;
            clone.TenantGuid = TenantGuid;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.StockBalance data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            StockItemGuid = data.StockItemGuid;
            StockItemVariantGuid = data.StockItemVariantGuid;
            StorageLocationGuid = data.StorageLocationGuid;
            Quantity = data.Quantity;
            BatchNumber = data.BatchNumber;
            ExpiryDate = data.ExpiryDate;
            TenantGuid = data.TenantGuid;
        }
    }
}
