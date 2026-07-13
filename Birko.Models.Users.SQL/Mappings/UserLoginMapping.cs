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

            // CR-M229: (Provider, ProviderKey) is the natural key of an external auth identity and must
            // be unique to stop two rows claiming the same identity. The fluent mapping expresses it as a
            // composite index (shared index name) — but note index/unique mapping metadata is advisory
            // (NOT emitted by ModelMapRegistry.ApplyToDatabase; see FieldBuilder), and the framework has
            // no composite-UNIQUE primitive (IsUnique is single-column). Enforce the UNIQUE constraint on
            // this pair at the DB via a migration; this mapping records the intent/natural key.
            map.Property(x => x.Provider).HasPrecision(50).HasIndex("UX_UserLogin_Provider_ProviderKey", order: 0);
            map.Property(x => x.ProviderKey).HasPrecision(256).HasIndex("UX_UserLogin_Provider_ProviderKey", order: 1);
            map.Property(x => x.PasswordHash).HasPrecision(256);
            map.Property(x => x.RefreshToken).HasPrecision(512);
            map.Property(x => x.DisplayName).HasPrecision(256);
        }
    }
}
