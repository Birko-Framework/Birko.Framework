using Birko.Models.Customers;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Customers.SQL.Mappings
{
    public class CustomerMapping : IModelMapping<Customer>
    {
        public void Configure(ModelMap<Customer> map)
        {
            map.ToTable("Customers")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // CR-M219: bound the string columns (previously unmapped → unbounded nvarchar(max),
            // unindexable), matching ContactPersonMapping / Pricing.SQL precision conventions.
            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Code).HasPrecision(64);
            map.Property(x => x.Email).HasPrecision(256);
            map.Property(x => x.Phone).HasPrecision(64);
            map.Property(x => x.Website).HasPrecision(256);
            map.Property(x => x.TaxId).HasPrecision(32);
            map.Property(x => x.VatId).HasPrecision(32);
        }
    }
}
