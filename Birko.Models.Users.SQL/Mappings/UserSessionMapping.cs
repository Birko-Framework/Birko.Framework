using Birko.Models.Users;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Users.SQL.Mappings
{
    public class UserSessionMapping : IModelMapping<UserSession>
    {
        public void Configure(ModelMap<UserSession> map)
        {
            map.ToTable("UserSessions")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            // TASK-805: a refresh finds its session by the token's hash, so the hash is the lookup key; UNIQUE, as two rows
            // holding one token would make a refresh pick a session arbitrarily. SHA-256 hex is 64 characters.
            map.Property(x => x.TokenHash).HasMaxLength(64).HasUniqueIndex("UX_UserSessions_TokenHash");
            // Reuse detection looks a presented token up here when no row holds it as current.
            map.Property(x => x.PreviousTokenHash).HasMaxLength(64).HasIndex("IX_UserSessions_PreviousTokenHash");
            // A user's sessions: list, revoke all, prune at sign-in.
            map.Property(x => x.UserGuid).HasIndex("IX_UserSessions_UserGuid");
            map.Property(x => x.DeviceLabel).HasMaxLength(256);
            map.Property(x => x.RevokedReason).HasMaxLength(64);
        }
    }
}
