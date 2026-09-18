using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Inventory.SQL.Mappings
{
    /// <summary>
    /// Mapping for <see cref="StockBalance"/> (TASK-444).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A new table name, unlike its three siblings — and not because no old table existed.</b> The
    /// retired schema did have one: <c>Warehouse.ItemRepository</c> carried <c>[Table("ItemRepositories")]</c>.
    /// It is not reused because this model is <b>not column-compatible</b> with it. A mapping's column name
    /// defaults to the property name, and every coordinate was renamed in the move to Inventory —
    /// <c>ItemGuid</c> → <c>StockItemGuid</c>, <c>RepositoryGuid</c> → <c>StorageLocationGuid</c>,
    /// <c>AgendaGuid</c> → <c>TenantGuid</c>, <c>Batch</c> → <c>BatchNumber</c>, <c>Amount</c> →
    /// <c>Quantity</c>. Pointing at <c>ItemRepositories</c> would promise a drop-in compatibility that six
    /// renamed columns break. <c>StockItem</c> → <c>Items</c> and <c>StorageLocation</c> →
    /// <c>Repositories</c> keep their legacy names precisely because their columns <i>did</i> survive.
    /// </para>
    /// <para>
    /// <b>Why a mapping at all.</b> <c>Quantity</c> is a <c>decimal</c>, and an unmapped decimal takes
    /// whatever the provider defaults to — for several that is 18,2, which silently truncates the
    /// fractional quantities this domain exists to track. 22,6 matches <c>InventoryDocumentLineMapping</c>
    /// and is the framework's canonical pair (<c>ValueData.StoreDecimalPrecision</c> /
    /// <c>StoreDecimalPlaces</c>), which the retired models applied through
    /// <c>[PrecisionField]</c>/<c>[ScaleField]</c>. Held as local constants here to match the sibling
    /// mappings rather than to take a dependency on <c>Birko.Models</c> for two integers.
    /// </para>
    /// </remarks>
    public class StockBalanceMapping : IModelMapping<StockBalance>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<StockBalance> map)
        {
            map.ToTable("StockBalances")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Quantity).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.BatchNumber).HasPrecision(256);
        }
    }
}
