using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserRoleMapping : IModelMapping<UserRole>
    {
        public void Configure(ModelMap<UserRole> map)
        {
            map.ToTable("UserRoles")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // CR-L324: index the FK lookup columns — "a user's roles" / "a role's users" would otherwise
            // table-scan. (Index metadata is advisory, consumed by migrations.)
            map.Property(x => x.UserGuid).HasIndex("IX_UserRoles_UserGuid");
            map.Property(x => x.RoleGuid).HasIndex("IX_UserRoles_RoleGuid");
        }
    }
}
