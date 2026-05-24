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
        }
    }
}
