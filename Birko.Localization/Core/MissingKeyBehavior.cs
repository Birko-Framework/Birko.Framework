namespace Birko.Localization;

/// <summary>
/// Defines behavior when a translation key is not found.
/// </summary>
public enum MissingKeyBehavior
{
    /// <summary>Returns the key string as-is.</summary>
    ReturnKey,

    /// <summary>Returns an empty string.</summary>
    ReturnEmpty,

    /// <summary>Throws a <see cref="System.Collections.Generic.KeyNotFoundException"/>.</summary>
    ThrowException
}
