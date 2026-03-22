using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.SQL.Mappings
{
    public class RoleMapping : IModelMapping<Role>
    {
        public void Configure(ModelMap<Role> map)
        {
            map.ToTable("Roles")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Name).HasPrecision(100).IsUnique();
            map.Property(x => x.Description).HasPrecision(500);
        }
    }
}
