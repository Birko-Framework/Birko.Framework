using System.Collections.Generic;

namespace Birko.Data.Localization.Models;

/// <summary>
/// Marker interface for entities that have fields which can be localized (translated).
/// Implement this on any model whose properties (e.g., Name, Description) should support multiple languages.
/// </summary>
public interface ILocalizable
{
    /// <summary>
    /// Returns the names of properties that are localizable.
    /// These must be string properties on the implementing class.
    /// </summary>
    /// <returns>A collection of property names that support localization.</returns>
    IReadOnlyList<string> GetLocalizableFields();
}
