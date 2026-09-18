using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Inventory.SQL.Mappings
{
    /// <summary>
    /// Mapping for <see cref="StockMovement"/> (TASK-444).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closed a real exposure rather than filling in a blank: <see cref="StockMovement.Quantity"/> and
    /// <see cref="StockMovement.UnitPrice"/> are <c>decimal</c>, and an unmapped decimal takes the
    /// provider's default — 18,2 on several — which silently truncates the fractional quantities and
    /// unit prices this domain exists to track. It is the same 22,6 used by
    /// <c>InventoryDocumentLineMapping</c> and <c>StockBalanceMapping</c>, so a quantity survives a
    /// document line, a movement and a balance identically.
    /// </para>
    /// <para>
    /// <b>A new table name, though a retired one exists.</b> <c>Warehouse.ItemRepositoryMovement</c>
    /// carried <c>[Table("ItemRepositoryMovements")]</c>, but that schema is not reachable from this
    /// model: a column name defaults to the property name, and the movement was reshaped as well as
    /// renamed — one <c>RepositoryGuid</c> became <c>FromLocationGuid</c> + <c>ToLocationGuid</c>,
    /// <c>Amount</c> became <c>Quantity</c>, <c>Date</c> became <c>MovementDate</c>, <c>AgendaGuid</c>
    /// became <c>TenantGuid</c>, and the retired type's six price/VAT columns have no counterpart here at
    /// all. Reusing the old name would promise a compatibility that nothing about the shape supports.
    /// </para>
    /// </remarks>
    public class StockMovementMapping : IModelMapping<StockMovement>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<StockMovement> map)
        {
            map.ToTable("StockMovements")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Quantity).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.UnitPrice).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.BatchNumber).HasPrecision(256);
        }
    }
}
