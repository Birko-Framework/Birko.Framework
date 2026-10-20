using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserLoginMapping : IModelMapping<UserLogin>
    {
        public void Configure(ModelMap<UserLogin> map)
        {
            map.ToTable("UserLogins")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // CR-M229: (Provider, ProviderKey) is the natural key of an external auth identity, and it is UNIQUE so two
            // rows can never claim the same identity. A sign-in looks the login up by exactly this pair. TASK-595: this
            // was declared as a plain HasIndex that nothing applied, so the constraint existed on no database.
            map.Property(x => x.Provider).HasMaxLength(50).HasUniqueIndex("UX_UserLogin_Provider_ProviderKey", order: 0);
            map.Property(x => x.ProviderKey).HasMaxLength(256).HasUniqueIndex("UX_UserLogin_Provider_ProviderKey", order: 1);
            // CR-L324: index the FK lookup column (a user's logins).
            map.Property(x => x.UserGuid).HasIndex("IX_UserLogins_UserGuid");
            map.Property(x => x.PasswordHash).HasMaxLength(256);
            map.Property(x => x.RefreshToken).HasMaxLength(512);
            map.Property(x => x.DisplayName).HasMaxLength(256);
        }
    }
}
