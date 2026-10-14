using Birko.Models.SQL.Mapping;
using Birko.Security.Grants.SQL.Mappings;

namespace Birko.Security.Grants.SQL;

/// <summary>Registers the grant mappings. Call before the first grant store is used, then <c>ApplyToDatabase()</c>.</summary>
public static class GrantSqlMappings
{
    /// <summary>Adds <see cref="ResourceGrantMapping"/> and <see cref="InvitationMapping"/> to <paramref name="registry"/>.</summary>
    public static ModelMapRegistry Register(ModelMapRegistry registry)
    {
        registry.Register(new ResourceGrantMapping());
        registry.Register(new InvitationMapping());
        return registry;
    }
}
