using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Resolves culture from the current thread's <see cref="CultureInfo.CurrentUICulture"/>.
/// </summary>
public sealed class ThreadCultureResolver : ICultureResolver
{
    private readonly CultureInfo _defaultCulture;

    public ThreadCultureResolver(CultureInfo? defaultCulture = null)
    {
        _defaultCulture = defaultCulture ?? CultureInfo.InvariantCulture;
    }

    public CultureInfo GetCurrentCulture() => CultureInfo.CurrentUICulture;

    public CultureInfo GetDefaultCulture() => _defaultCulture;
}
