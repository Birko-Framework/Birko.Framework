using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.SQL.Mappings
{
    public class RolePermissionMapping : IModelMapping<RolePermission>
    {
        public void Configure(ModelMap<RolePermission> map)
        {
            map.ToTable("RolePermissions")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.PermissionCode).HasPrecision(256);
        }
    }
}
