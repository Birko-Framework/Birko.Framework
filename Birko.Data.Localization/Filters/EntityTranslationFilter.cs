using System;
using System.Linq.Expressions;
using Birko.Data.Localization.Models;

namespace Birko.Data.Localization.Filters;

/// <summary>
/// Filter for querying entity translations by entity, field, or culture.
/// </summary>
public class EntityTranslationFilter
{
    public Guid? EntityGuid { get; set; }
    public string? EntityType { get; set; }
    public string? FieldName { get; set; }
    public string? Culture { get; set; }

    public Expression<Func<EntityTranslationModel, bool>> ToExpression()
    {
        return t =>
            (EntityGuid == null || t.EntityGuid == EntityGuid) &&
            (EntityType == null || t.EntityType == EntityType) &&
            (FieldName == null || t.FieldName == FieldName) &&
            (Culture == null || t.Culture == Culture);
    }

    /// <summary>Creates a filter for all translations of a specific entity.</summary>
    public static EntityTranslationFilter ByEntity(Guid entityGuid)
        => new() { EntityGuid = entityGuid };

    /// <summary>Creates a filter for all translations of a specific entity and culture.</summary>
    public static EntityTranslationFilter ByEntityAndCulture(Guid entityGuid, string culture)
        => new() { EntityGuid = entityGuid, Culture = culture };

    /// <summary>Creates a filter for a specific entity, field, and culture.</summary>
    public static EntityTranslationFilter ByEntityFieldAndCulture(Guid entityGuid, string fieldName, string culture)
        => new() { EntityGuid = entityGuid, FieldName = fieldName, Culture = culture };

    /// <summary>Creates a filter for all translations of a specific entity type.</summary>
    public static EntityTranslationFilter ByEntityType(string entityType)
        => new() { EntityType = entityType };

    /// <summary>Creates a filter for all translations of a specific entity type and culture.</summary>
    public static EntityTranslationFilter ByEntityTypeAndCulture(string entityType, string culture)
        => new() { EntityType = entityType, Culture = culture };
}
