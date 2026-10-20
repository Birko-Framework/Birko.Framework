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

            map.Property(x => x.FirstName).HasMaxLength(100);
            map.Property(x => x.LastName).HasMaxLength(100);
            map.Property(x => x.DisplayName).HasMaxLength(200);
            map.Property(x => x.Phone).HasMaxLength(20);
            map.Property(x => x.AvatarUrl).HasMaxLength(500);
            map.Property(x => x.Locale).HasMaxLength(10);
            map.Property(x => x.TimeZone).HasMaxLength(50);
            map.Property(x => x.Bio).HasMaxLength(500);
        }
    }
}
