using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Inventory.SQL.Mappings
{
    public class InventoryDocumentLineMapping : IModelMapping<InventoryDocumentLine>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<InventoryDocumentLine> map)
        {
            map.ToTable("WareHouseDocumentItems")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Quantity).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.UnitPrice).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.UnitPriceVAT).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.VAT).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.TotalPrice).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.TotalPriceVAT).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }
}
