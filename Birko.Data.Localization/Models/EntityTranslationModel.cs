using System;
using Birko.Data.Models;

namespace Birko.Data.Localization.Models;

/// <summary>
/// Stores a single translated value for a specific entity field and culture.
/// Each row represents one (EntityGuid, EntityType, FieldName, Culture) combination.
/// </summary>
public class EntityTranslationModel : AbstractModel
{
    /// <summary>The GUID of the entity this translation belongs to.</summary>
    public Guid EntityGuid { get; set; }

    /// <summary>The type name of the entity (e.g., "Product", "Category").</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The property name being translated (e.g., "Name", "Description").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>The culture code (e.g., "en", "sk", "de-DE").</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>The translated value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>When the translation was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    public override AbstractModel CopyTo(AbstractModel? clone = null)
    {
        clone ??= new EntityTranslationModel();
        base.CopyTo(clone);
        if (clone is EntityTranslationModel target)
        {
            target.EntityGuid = EntityGuid;
            target.EntityType = EntityType;
            target.FieldName = FieldName;
            target.Culture = Culture;
            target.Value = Value;
            target.UpdatedAt = UpdatedAt;
        }
        return clone;
    }
}
