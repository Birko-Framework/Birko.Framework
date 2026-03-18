using System;
using Birko.Data.Models;

namespace Birko.Localization.Data;

/// <summary>
/// Persisted translation entity. Stores a single key-value translation for a specific culture.
/// </summary>
public class TranslationModel : AbstractModel
{
    /// <summary>The translation key (e.g., "greeting", "errors.notFound").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The culture name (e.g., "en", "sk", "sk-SK").</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>The translated value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional namespace/module for scoping translations (e.g., "orders", "auth").</summary>
    public string? Namespace { get; set; }

    /// <summary>When the translation was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    public override AbstractModel CopyTo(AbstractModel? clone = null)
    {
        clone ??= new TranslationModel();
        base.CopyTo(clone);
        if (clone is TranslationModel target)
        {
            target.Key = Key;
            target.Culture = Culture;
            target.Value = Value;
            target.Namespace = Namespace;
            target.UpdatedAt = UpdatedAt;
        }
        return clone;
    }
}
