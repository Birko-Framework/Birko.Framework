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
        public void Configure(ModelMap<UnitConversion> map)
        {
            map.ToTable("UnitConversions")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Factor).HasPrecision(18).HasScale(6);
        }
    }
}
