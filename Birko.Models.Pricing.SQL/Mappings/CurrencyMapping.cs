using Birko.Models.Pricing;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Pricing.SQL.Mappings
{
    public class CurrencyMapping : IModelMapping<Currency>
    {
        public void Configure(ModelMap<Currency> map)
        {
            map.ToTable("Currencies")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Code).HasPrecision(8).IsUnique();
            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Symbol).HasPrecision(8);
        }
    }

    public class TaxMapping : IModelMapping<Tax>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<Tax> map)
        {
            map.ToTable("Taxes")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.ShortCut).HasPrecision(16);
            map.Property(x => x.Percentage).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }

    public class PriceGroupMapping : IModelMapping<PriceGroup>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<PriceGroup> map)
        {
            map.ToTable("PriceGroups")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Percentage).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }
}
