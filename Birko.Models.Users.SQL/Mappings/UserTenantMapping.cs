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
        }
    }
}
