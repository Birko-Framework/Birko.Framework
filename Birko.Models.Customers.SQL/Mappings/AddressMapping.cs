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

            // CR-M220: bound the address string columns (were unmapped → unbounded), mirroring
            // ContactPersonMapping in this file.
            map.Property(x => x.Name).HasMaxLength(256);
            map.Property(x => x.Street).HasMaxLength(256);
            map.Property(x => x.StreetNumber).HasMaxLength(64);
            map.Property(x => x.City).HasMaxLength(256);
            map.Property(x => x.ZIP).HasMaxLength(16);
            map.Property(x => x.District).HasMaxLength(256);
            map.Property(x => x.Region).HasMaxLength(256);
            map.Property(x => x.Country).HasMaxLength(128);
            map.Property(x => x.Phone).HasMaxLength(64);
            map.Property(x => x.Email).HasMaxLength(256);
        }
    }

    public class InvoiceAddressMapping : IModelMapping<InvoiceAddress>
    {
        public void Configure(ModelMap<InvoiceAddress> map)
        {
            map.ToTable("InvoiceAddresses")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // CR-M220: InvoiceAddress : Address — re-map the inherited address columns here (each
            // mapping configures its own table) plus the invoice-specific identifiers.
            map.Property(x => x.Name).HasMaxLength(256);
            map.Property(x => x.Street).HasMaxLength(256);
            map.Property(x => x.StreetNumber).HasMaxLength(64);
            map.Property(x => x.City).HasMaxLength(256);
            map.Property(x => x.ZIP).HasMaxLength(16);
            map.Property(x => x.District).HasMaxLength(256);
            map.Property(x => x.Region).HasMaxLength(256);
            map.Property(x => x.Country).HasMaxLength(128);
            map.Property(x => x.Phone).HasMaxLength(64);
            map.Property(x => x.Email).HasMaxLength(256);
            map.Property(x => x.BIN).HasMaxLength(64);
            map.Property(x => x.TIN).HasMaxLength(64);
            map.Property(x => x.VATIN).HasMaxLength(64);
            map.Property(x => x.BankAccount).HasMaxLength(64);
        }
    }

    public class ContactPersonMapping : IModelMapping<ContactPerson>
    {
        public void Configure(ModelMap<ContactPerson> map)
        {
            map.ToTable("ContactPersons")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasMaxLength(256);
            map.Property(x => x.Position).HasMaxLength(256);
            map.Property(x => x.Phone).HasMaxLength(256);
            map.Property(x => x.Email).HasMaxLength(256);
        }
    }
}
