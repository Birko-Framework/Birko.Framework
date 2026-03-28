using Birko.Models.SQL.Mapping;
using Birko.Models.Accounting;
using PricingCurrency = Birko.Models.Pricing.Currency;
using PricingTax = Birko.Models.Pricing.Tax;
using PricingPriceGroup = Birko.Models.Pricing.PriceGroup;

namespace Birko.Models.SQL.Mappings
{
    public class CurrencyMapping : IModelMapping<PricingCurrency>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<PricingCurrency> map)
        {
            map.ToTable("Currencies")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Symbol).HasPrecision(8);
            map.Property(x => x.FromRate).HasColumnName("FromRate").HasPrecision(DecimalPrecision).HasScale(DecimalScale);
            map.Property(x => x.ToRate).HasColumnName("ToRate").HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }

    public class TaxMapping : IModelMapping<PricingTax>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<PricingTax> map)
        {
            map.ToTable("Taxes")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.ShortCut).HasPrecision(16);
            map.Property(x => x.Percentage).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
        }
    }

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

    public class PriceGroupMapping : IModelMapping<PricingPriceGroup>
    {
        private const int DecimalPrecision = 22;
        private const int DecimalScale = 6;

        public void Configure(ModelMap<PricingPriceGroup> map)
        {
            map.ToTable("PriceGroups")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Percentage).HasPrecision(DecimalPrecision).HasScale(DecimalScale);
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
