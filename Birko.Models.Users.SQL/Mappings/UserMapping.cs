using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserMapping : IModelMapping<User>
    {
        public void Configure(ModelMap<User> map)
        {
            map.ToTable("Users")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.UserName).HasPrecision(256).IsUnique();
            map.Property(x => x.Email).HasPrecision(256).IsUnique();
        }
    }
}
