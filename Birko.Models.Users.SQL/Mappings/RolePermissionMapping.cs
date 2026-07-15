using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class RolePermissionMapping : IModelMapping<RolePermission>
    {
        public void Configure(ModelMap<RolePermission> map)
        {
            map.ToTable("RolePermissions")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.PermissionCode).HasPrecision(256);
            // CR-L324: index the FK lookup column (a role's permissions). Index metadata is advisory.
            map.Property(x => x.RoleGuid).HasIndex("IX_RolePermissions_RoleGuid");
        }
    }
}
