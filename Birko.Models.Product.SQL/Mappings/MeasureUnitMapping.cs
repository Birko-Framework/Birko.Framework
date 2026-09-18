using Birko.Models.Product;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Product.SQL.Mappings
{
    public class MeasureUnitMapping : IModelMapping<MeasureUnit>
    {
        public void Configure(ModelMap<MeasureUnit> map)
        {
            map.ToTable("MeasureUnits")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Code).HasPrecision(50).IsUnique();
            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Symbol).HasPrecision(20);
        }
    }

    public class UnitConversionMapping : IModelMapping<UnitConversion>
    {
        // CR-L317: align the decimal facet with the framework-wide convention used by the sibling SQL
        // mappings (Pricing.SQL Tax/PriceGroup.Percentage, Inventory.SQL line amounts) — precision 22,
        // scale 6 via shared constants, rather than a divergent precision 18.
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<UnitConversion> map)
        {
            map.ToTable("UnitConversions")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Factor).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }
}
