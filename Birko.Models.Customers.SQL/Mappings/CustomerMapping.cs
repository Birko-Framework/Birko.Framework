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
        }
    }
}
