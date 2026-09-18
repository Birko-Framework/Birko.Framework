using System.Globalization;

namespace Birko.Data.Localization.Models;

/// <summary>
/// Provides the current culture context for entity localization.
/// Inject this to control which language is used when reading/writing localized entities.
/// </summary>
public interface IEntityLocalizationContext
{
    /// <summary>
    /// The current culture to use for reading localized fields.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// The default culture in which the base entity fields are stored.
    /// Translations are not created for the default culture (the entity itself holds those values).
    /// </summary>
    CultureInfo DefaultCulture { get; }
}
