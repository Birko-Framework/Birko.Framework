using Birko.Models.SQL.Mapping;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.SQL.Mappings;

/// <summary>SQL schema of <see cref="Invitation"/>: table <c>BirkoInvitations</c>.</summary>
public sealed class InvitationMapping : IModelMapping<Invitation>
{
    public const string Table = "BirkoInvitations";

    public void Configure(ModelMap<Invitation> map)
    {
        map.ToTable(Table)
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);

        // A token hash names exactly one invitation: lookups by it must never see two.
        map.Property(x => x.TokenHash).IsRequired().IsUnique();
        // Declared here, so MySQL and SQL Server bound the column instead of a type they cannot index (TASK-693).
        map.Property(x => x.Email).IsRequired()
            .HasIndex(GrantSqlIndexes.InvitationsByEmail);
        map.Property(x => x.ResourceType).IsRequired();
        map.Property(x => x.Role).IsRequired();
    }
}
