using Birko.Models.SQL.Mapping;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.SQL.Mappings;

/// <summary>SQL schema of <see cref="ResourceGrant"/>: table <c>BirkoResourceGrants</c>.</summary>
public sealed class ResourceGrantMapping : IModelMapping<ResourceGrant>
{
    public const string Table = "BirkoResourceGrants";

    public void Configure(ModelMap<ResourceGrant> map)
    {
        map.ToTable(Table)
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);

        // The lookup indexes are declared here, so the schema knows these columns are index keys (rule 37): MySQL and
        // SQL Server bound them instead of LONGTEXT / NVARCHAR(MAX), which neither can index (TASK-693, measured).
        map.Property(x => x.UserGuid).IsRequired()
            .HasIndex(GrantSqlIndexes.GrantsByUser, 0);
        map.Property(x => x.ResourceType).IsRequired()
            .HasIndex(GrantSqlIndexes.GrantsByUser, 1)
            .HasIndex(GrantSqlIndexes.GrantsByResource, 0);
        map.Property(x => x.ResourceId)
            .HasIndex(GrantSqlIndexes.GrantsByResource, 1);
        map.Property(x => x.Role).IsRequired();
    }
}
