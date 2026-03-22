using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.SQL.Mappings
{
    public class UserLoginMapping : IModelMapping<UserLogin>
    {
        public void Configure(ModelMap<UserLogin> map)
        {
            map.ToTable("UserLogins")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Provider).HasPrecision(50);
            map.Property(x => x.ProviderKey).HasPrecision(256);
            map.Property(x => x.PasswordHash).HasPrecision(256);
            map.Property(x => x.RefreshToken).HasPrecision(512);
            map.Property(x => x.DisplayName).HasPrecision(256);
        }
    }
}
