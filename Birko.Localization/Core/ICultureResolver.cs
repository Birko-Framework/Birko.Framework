using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Determines the current and default cultures for localization.
/// </summary>
public interface ICultureResolver
{
    /// <summary>Returns the current culture for the active context (thread, request, etc.).</summary>
    CultureInfo GetCurrentCulture();

    /// <summary>Returns the default/fallback culture.</summary>
    CultureInfo GetDefaultCulture();
}
