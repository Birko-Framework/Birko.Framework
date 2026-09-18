using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Birko.Localization;

/// <summary>
/// Loads translations from JSON files. Supports flat and nested key formats.
/// File naming convention: {culture}.json (e.g., en.json, sk.json, sk-SK.json).
/// </summary>
public sealed class JsonTranslationProvider : ITranslationProvider
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public JsonTranslationProvider(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        if (!Directory.Exists(basePath))
        {
            throw new DirectoryNotFoundException($"Localization directory not found: {basePath}");
        }
        _basePath = basePath;
    }

    public string? GetTranslation(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dict = LoadCulture(culture.Name);
        return dict.TryGetValue(key, out var value) ? value : null;
    }

    public IReadOnlyList<CultureInfo> GetSupportedCultures()
    {
        return Directory.GetFiles(_basePath, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            // CR-L273: empty file names (e.g. a bare ".json") are skipped here, so the Select below no
            // longer needs a dead IsNullOrEmpty→InvariantCulture branch — every name reaching it is non-empty.
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name =>
            {
                try
                {
                    return CultureInfo.GetCultureInfo(name);
                }
                catch (CultureNotFoundException)
                {
                    return null;
                }
            })
            .Where(c => c != null)
            .ToList()!;
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
            var filePath = Path.Combine(_basePath, $"{name}.json");
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenJson(doc.RootElement, string.Empty, dict);
            return dict;
        });
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJson(property.Value, key, result);
                }
                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString()!;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.ToString();
                break;
        }
    }
}
