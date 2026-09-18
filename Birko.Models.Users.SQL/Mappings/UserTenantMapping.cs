using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserTenantMapping : IModelMapping<UserTenant>
    {
        public void Configure(ModelMap<UserTenant> map)
        {
            map.ToTable("UserTenants")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // CR-L324: index the FK lookup column (a user's tenants). Index metadata is advisory.
            map.Property(x => x.UserGuid).HasIndex("IX_UserTenants_UserGuid");
        }
    }
}
