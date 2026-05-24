using Birko.Models.Customers;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Customers.SQL.Mappings
{
    public class AddressMapping : IModelMapping<Address>
    {
        public void Configure(ModelMap<Address> map)
        {
            map.ToTable("Addresses")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);
        }
    }

    public class InvoiceAddressMapping : IModelMapping<InvoiceAddress>
    {
        public void Configure(ModelMap<InvoiceAddress> map)
        {
            map.ToTable("InvoiceAddresses")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);
        }
    }

    public class ContactPersonMapping : IModelMapping<ContactPerson>
    {
        public void Configure(ModelMap<ContactPerson> map)
        {
            map.ToTable("ContactPersons")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(256);
            map.Property(x => x.Position).HasPrecision(256);
            map.Property(x => x.Phone).HasPrecision(256);
            map.Property(x => x.Email).HasPrecision(256);
        }
    }
}
