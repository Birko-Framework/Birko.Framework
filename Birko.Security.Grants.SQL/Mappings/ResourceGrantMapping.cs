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

        map.Property(x => x.UserGuid).IsRequired();
        map.Property(x => x.ResourceType).IsRequired();
        map.Property(x => x.Role).IsRequired();
    }
}
