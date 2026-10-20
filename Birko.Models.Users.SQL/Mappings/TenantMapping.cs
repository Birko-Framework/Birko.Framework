using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class TenantMapping : IModelMapping<Tenant>
    {
        public void Configure(ModelMap<Tenant> map)
        {
            map.ToTable("Tenants")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasMaxLength(256);
            map.Property(x => x.Description).HasMaxLength(1000);
        }
    }
}
