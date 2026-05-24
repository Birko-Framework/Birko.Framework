using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserProfileMapping : IModelMapping<UserProfile>
    {
        public void Configure(ModelMap<UserProfile> map)
        {
            map.ToTable("UserProfiles")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid)
                .HasUnique(x => x.UserGuid);

            map.Property(x => x.FirstName).HasPrecision(100);
            map.Property(x => x.LastName).HasPrecision(100);
            map.Property(x => x.DisplayName).HasPrecision(200);
            map.Property(x => x.Phone).HasPrecision(20);
            map.Property(x => x.AvatarUrl).HasPrecision(500);
            map.Property(x => x.Locale).HasPrecision(10);
            map.Property(x => x.TimeZone).HasPrecision(50);
            map.Property(x => x.Bio).HasPrecision(500);
        }
    }
}
