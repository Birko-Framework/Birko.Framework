using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

namespace Birko.Localization;

/// <summary>
/// Loads translations from .resx XML files.
/// File naming: {baseName}.{culture}.resx (e.g., Messages.sk.resx).
/// Default culture file: {baseName}.resx (e.g., Messages.resx).
/// </summary>
public sealed class ResxTranslationProvider : ITranslationProvider
{
    private readonly string _basePath;
    private readonly string _baseName;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ResxTranslationProvider(string basePath, string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        if (!Directory.Exists(basePath))
        {
            throw new DirectoryNotFoundException($"Localization directory not found: {basePath}");
        }
        _basePath = basePath;
        _baseName = baseName;
    }

    public string? GetTranslation(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dict = LoadCulture(culture.Name);
        return dict.TryGetValue(key, out var value) ? value : null;
    }

    public IReadOnlyList<CultureInfo> GetSupportedCultures()
    {
        var pattern = $"{_baseName}.*.resx";
        var cultures = Directory.GetFiles(_basePath, pattern)
            .Select(f =>
            {
                var fileName = Path.GetFileNameWithoutExtension(f);
                var culturePart = fileName.Substring(_baseName.Length + 1);
                try
                {
                    return CultureInfo.GetCultureInfo(culturePart);
                }
                catch (CultureNotFoundException)
                {
                    return null;
                }
            })
            .Where(c => c != null)
            .ToList<CultureInfo?>();

        // Check for default culture file (no culture suffix)
        var defaultFile = Path.Combine(_basePath, $"{_baseName}.resx");
        if (File.Exists(defaultFile))
        {
            cultures.Insert(0, CultureInfo.InvariantCulture);
        }

        return cultures!;
    }

    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return LoadCulture(culture.Name);
    }

    private IReadOnlyDictionary<string, string> LoadCulture(string cultureName)
    {
        return _cache.GetOrAdd(cultureName, name =>
        {
            var filePath = string.IsNullOrEmpty(name)
                ? Path.Combine(_basePath, $"{_baseName}.resx")
                : Path.Combine(_basePath, $"{_baseName}.{name}.resx");

            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string>();
            }

            var doc = XDocument.Load(filePath);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var data in doc.Descendants("data"))
            {
                var key = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (key != null && value != null)
                {
                    dict[key] = value;
                }
            }

            return dict;
        });
    }
}
