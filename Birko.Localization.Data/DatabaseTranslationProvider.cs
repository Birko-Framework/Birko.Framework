using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Localization.Data;

/// <summary>
/// Translation provider backed by any Birko.Data async bulk store.
/// Supports optional in-memory caching with configurable TTL.
/// Works with any store backend (SQL, MongoDB, ElasticSearch, JSON, etc.).
/// </summary>
public class DatabaseTranslationProvider : ITranslationProvider
{
    private readonly IAsyncBulkReadStore<TranslationModel> _store;
    private readonly string? _namespace;
    private readonly TimeSpan _cacheDuration;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CacheEntry
    {
        public IReadOnlyDictionary<string, string> Translations { get; init; } = null!;
        public DateTime LoadedAt { get; init; }
    }

    /// <summary>
    /// Creates a database translation provider.
    /// </summary>
    /// <param name="store">Any async bulk store for TranslationModel.</param>
    /// <param name="namespace">Optional namespace to scope translations.</param>
    /// <param name="cacheDuration">How long to cache loaded translations. Default: 5 minutes. Use TimeSpan.Zero to disable caching.</param>
    public DatabaseTranslationProvider(
        IAsyncBulkReadStore<TranslationModel> store,
        string? @namespace = null,
        TimeSpan? cacheDuration = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _namespace = @namespace;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Gets a translation for the given key and culture.
    /// </summary>
    /// <remarks>
    /// CR-M198: this synchronous member blocks on async store I/O (<c>.GetAwaiter().GetResult()</c>) on a
    /// cache miss. Avoid calling it on a thread with a single-threaded <see cref="System.Threading.SynchronizationContext"/>
    /// (legacy ASP.NET, WPF/WinForms UI thread) — the store continuation may need that context and deadlock.
    /// Prefer <see cref="GetTranslationAsync"/>.
    /// </remarks>
    public string? GetTranslation(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dict = GetCultureTranslations(culture.Name);
        return dict.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Gets the cultures with translations in the store.
    /// </summary>
    /// <remarks>
    /// CR-M198: blocks on async store I/O — see <see cref="GetTranslation"/>. Avoid on UI /
    /// single-threaded-sync-context threads; there is no async counterpart for this member, so cache the
    /// result rather than calling it on a hot/UI path.
    /// </remarks>
    public IReadOnlyList<CultureInfo> GetSupportedCultures()
    {
        var models = LoadAllAsync().GetAwaiter().GetResult();
        return models
            .Select(m => m.Culture)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                try { return CultureInfo.GetCultureInfo(name); }
                catch (CultureNotFoundException) { return null; }
            })
            .Where(c => c != null)
            .ToList()!;
    }

    /// <summary>
    /// Gets all translations for the given culture.
    /// </summary>
    /// <remarks>
    /// CR-M198: blocks on async store I/O on a cache miss — see <see cref="GetTranslation"/>. Prefer
    /// <see cref="GetAllAsync"/> on UI / single-threaded-sync-context threads.
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return GetCultureTranslations(culture.Name);
    }

    /// <summary>
    /// Asynchronously gets a translation for the given key and culture.
    /// </summary>
    public async Task<string?> GetTranslationAsync(string key, CultureInfo culture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dict = await GetCultureTranslationsAsync(culture.Name, ct);
        return dict.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Asynchronously gets all translations for the given culture.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CultureInfo culture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return await GetCultureTranslationsAsync(culture.Name, ct);
    }

    /// <summary>
    /// Invalidates the cache for a specific culture, or all cultures if null.
    /// Call after updating translations in the database.
    /// </summary>
    public void InvalidateCache(string? cultureName = null)
    {
        if (cultureName != null)
        {
            _cache.TryRemove(cultureName, out _);
        }
        else
        {
            _cache.Clear();
        }
    }

    private IReadOnlyDictionary<string, string> GetCultureTranslations(string cultureName)
    {
        return GetCultureTranslationsAsync(cultureName).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyDictionary<string, string>> GetCultureTranslationsAsync(string cultureName, CancellationToken ct = default)
    {
        if (_cacheDuration > TimeSpan.Zero
            && _cache.TryGetValue(cultureName, out var cached)
            && DateTime.UtcNow - cached.LoadedAt < _cacheDuration)
        {
            return cached.Translations;
        }

        var filter = BuildFilter(cultureName);
        var models = await _store.ReadAsync(filter, ct: ct);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (!string.IsNullOrEmpty(model.Key))
            {
                // CR-L279: coalesce a null Value so the IReadOnlyDictionary<string,string> contract stays
                // honest — a persisted/deserialized record with a null Value must not surface as a null
                // dictionary value and NRE a downstream consumer.
                dict[model.Key] = model.Value ?? string.Empty;
            }
        }

        if (_cacheDuration > TimeSpan.Zero)
        {
            _cache[cultureName] = new CacheEntry
            {
                Translations = dict,
                LoadedAt = DateTime.UtcNow
            };
        }

        return dict;
    }

    private async Task<IEnumerable<TranslationModel>> LoadAllAsync(CancellationToken ct = default)
    {
        Expression<Func<TranslationModel, bool>>? filter = _namespace != null
            ? t => t.Namespace == _namespace
            : null;
        return await _store.ReadAsync(filter, ct: ct);
    }

    private Expression<Func<TranslationModel, bool>> BuildFilter(string cultureName)
    {
        if (_namespace != null)
        {
            return t => t.Culture == cultureName && t.Namespace == _namespace;
        }
        return t => t.Culture == cultureName;
    }
}
