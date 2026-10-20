using Birko.Data.Localization.Boundaries;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Translations;

/// <summary>
/// <see cref="IEntityTranslations{T}"/> over an async bulk store of the translation model and the entity store
/// (TASK-682). Every method is a thin call into the producers the store wrappers use —
/// <see cref="LocalizedTranslationRows"/> for rows, <see cref="LocalizedFilterResolution"/> for lookups — so the API and
/// the wrappers cannot disagree about a key, a fallback or a batch.
/// </summary>
/// <remarks>
/// The entity store must not be a localized wrapper, anywhere in its <see cref="IStoreWrapper"/> chain: the lookups read
/// base values through it, and a localized wrapper would apply the ambient culture to that read. Other wrappers —
/// tenant, soft delete — are kept, so their scoping applies to the lookups.
/// </remarks>
public class EntityTranslations<T, TTranslation> : IEntityTranslations<T>
    where T : AbstractModel, ILocalizable
    where TTranslation : AbstractModel, IEntityTranslation
{
    private static readonly Type[] LocalizedWrappers =
    {
        typeof(LocalizedStoreWrapper<,,>),
        typeof(AsyncLocalizedStoreWrapper<,,>),
        typeof(LocalizedBulkStoreWrapper<,,>),
        typeof(AsyncLocalizedBulkStoreWrapper<,,>),
    };

    private readonly IAsyncBulkStore<T> _entities;
    private readonly IAsyncBulkStore<TTranslation> _translations;
    private readonly IEntityLocalizationContext _context;
    private readonly ILocalizedWriteBoundary _boundary;
    private readonly IReadOnlyList<string> _fields;
    private readonly string _entityType = LocalizedTranslationRows.EntityTypeOf<T>();

    /// <param name="entities">The entity store, unlocalized: read for the base values the lookups fall back to.</param>
    /// <param name="translations">The translation store the wrappers of <typeparamref name="T"/> use.</param>
    /// <param name="context">Only its <see cref="IEntityLocalizationContext.DefaultCulture"/> is read.</param>
    /// <param name="boundary">
    /// Makes each write one unit (TASK-683): <see cref="SetAsync"/>'s read and bulk calls, a chunked removal. Best effort
    /// when omitted. A write here never pairs with an entity write: wrap both in the caller's own transaction for that.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="entities"/> is, or wraps, a localized store wrapper.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> declares a localizable field that is not a public read/write string property, or
    /// <typeparamref name="TTranslation"/> does not expose its members as public properties.
    /// </exception>
    public EntityTranslations(
        IAsyncBulkStore<T> entities,
        IAsyncBulkStore<TTranslation> translations,
        IEntityLocalizationContext context,
        ILocalizedWriteBoundary? boundary = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _translations = translations ?? throw new ArgumentNullException(nameof(translations));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _boundary = boundary ?? BestEffortWriteBoundary.Instance;
        RefuseLocalizedWrapper(entities);
        _fields = LocalizedEntityFields.LocalizableFieldsOf(entities.CreateInstance());
        LocalizedTranslationRows.RequireQueryableModel<TTranslation>();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>>> GetAsync(
        IEnumerable<Guid> ids, CultureInfo culture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        RequireCulture(culture);
        var rows = await LocalizedTranslationRows.ReadManyAsync(_translations, ids, _entityType, culture.Name, ct);
        return Declared(rows);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetAllCulturesAsync(
        Guid id, CancellationToken ct = default)
    {
        RequireId(id);
        var rows = await LocalizedTranslationRows.ReadAllCulturesAsync(_translations, id, _entityType, ct);
        return Declared(rows);
    }

    public Task SetAsync(Guid id, CultureInfo culture, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default)
    {
        RequireId(id);
        RequireTranslatedCulture(culture);
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields.Keys)
        {
            RequireDeclared(field, nameof(fields));
        }
        if (fields.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _boundary.RunAsync(c => LocalizedTranslationRows.SaveAsync(_translations, id, _entityType, culture.Name, fields, c), ct);
    }

    public Task RemoveCultureAsync(Guid id, CultureInfo culture, CancellationToken ct = default)
    {
        RequireId(id);
        RequireTranslatedCulture(culture);
        return _boundary.RunAsync(c => LocalizedTranslationRows.DeleteCultureAsync(_translations, id, _entityType, culture.Name, c), ct);
    }

    public Task RemoveEntitiesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.Distinct().ToList();
        return list.Count == 0
            ? Task.CompletedTask
            : _boundary.RunAsync(c => LocalizedTranslationRows.DeleteManyAsync(_translations, list, _entityType, c), ct);
    }

    public async Task<Guid?> FindEntityAsync(string field, CultureInfo culture, string value, CancellationToken ct = default)
    {
        var found = await MatchingAsync(field, culture, value, exceptEntityGuid: null, limit: 2, ct);
        if (found.Count > 1)
        {
            // Rule 26: returning either one would be a wrong answer for the other.
            throw new InvalidOperationException(
                $"More than one {typeof(T).Name} reads '{value}' as {field} in '{culture.Name}': {found[0]} and {found[1]}.");
        }
        return found.Count == 0 ? null : found[0];
    }

    public async Task<bool> ExistsAsync(
        string field, CultureInfo culture, string value, Guid? exceptEntityGuid = null, CancellationToken ct = default)
        => (await MatchingAsync(field, culture, value, exceptEntityGuid, limit: 1, ct)).Count > 0;

    public async Task<IReadOnlyList<T>> LocalizeAsync(IEnumerable<T> entities, CultureInfo culture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        RequireCulture(culture);
        var list = entities.ToList();
        return IsDefault(culture)
            ? list
            : await LocalizedTranslationRows.LocalizeAllAsync(_translations, list, culture.Name, ct);
    }

    /// <summary>
    /// The one producer behind <see cref="FindEntityAsync"/> and <see cref="ExistsAsync"/>: <c>x.field == value</c>
    /// (<c>&amp;&amp; x.Guid != except</c>), rewritten for the culture by <see cref="LocalizedFilterResolution"/> exactly
    /// as a wrapper's read is, then read from the entity store up to <paramref name="limit"/> rows. On the default
    /// culture the filter tests the base column as written.
    /// </summary>
    private async Task<List<Guid>> MatchingAsync(
        string field, CultureInfo culture, string value, Guid? exceptEntityGuid, int limit, CancellationToken ct)
    {
        RequireCulture(culture);
        ArgumentNullException.ThrowIfNull(value);
        RequireDeclared(field, nameof(field));

        var x = Expression.Parameter(typeof(T), "x");
        Expression body = Expression.Equal(Expression.Property(x, field), Expression.Constant(value, typeof(string)));
        if (exceptEntityGuid is Guid except)
        {
            body = Expression.AndAlso(body, Expression.NotEqual(
                Expression.Property(x, nameof(AbstractModel.Guid)), Expression.Constant((Guid?)except, typeof(Guid?))));
        }
        var filter = Expression.Lambda<Func<T, bool>>(body, x);

        var queries = IsDefault(culture)
            ? LocalizedQueries<T>.One(filter)
            : await LocalizedFilterResolution.RewriteAsync(filter, _fields, _translations, _entities, culture.Name, ct);
        // TASK-686: the limit is counted across chunks, so Find still sees a second match in another chunk.
        return (await LocalizedChunkedReads.ReadUpToAsync(_entities, queries, limit, ct))
            .Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value).ToList();
    }

    private bool IsDefault(CultureInfo culture) => culture.Name == _context.DefaultCulture.Name;

    /// <summary>
    /// The declared fields of each group; a group left with none is dropped, so an entity or culture whose only rows are
    /// for undeclared fields reads as untranslated, as the wrappers treat it.
    /// </summary>
    private IReadOnlyDictionary<TKey, IReadOnlyDictionary<string, string>> Declared<TKey>(
        Dictionary<TKey, Dictionary<string, string?>> groups) where TKey : notnull
    {
        var result = new Dictionary<TKey, IReadOnlyDictionary<string, string>>();
        foreach (var (key, values) in groups)
        {
            var declared = LocalizedTranslationRows.OnlyFields(values, _fields);
            if (declared.Count > 0)
            {
                // IEntityTranslation.Value is a non-nullable string; a null is outside the model's contract.
                result[key] = declared.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);
            }
        }
        return result;
    }

    /// <summary>A culture with a name: rows are keyed by it, and the invariant culture's empty name keys nothing.</summary>
    private static void RequireCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrEmpty(culture.Name))
        {
            throw new ArgumentException("A named culture is required; the invariant culture keys no translation.", nameof(culture));
        }
    }

    /// <summary>
    /// A culture a translation row can hold. The default culture's values are the entity's own columns and the wrappers
    /// never read a row for it, so writing or removing one would report success and change nothing (rule 22).
    /// </summary>
    private void RequireTranslatedCulture(CultureInfo culture)
    {
        RequireCulture(culture);
        if (IsDefault(culture))
        {
            throw new ArgumentException(
                $"'{culture.Name}' is the default culture: its values are {typeof(T).Name}'s own columns. "
                + "Change them through the entity store.", nameof(culture));
        }
    }

    /// <summary>Rule 41: a field this type does not declare is refused, never read or written as if it were one.</summary>
    private void RequireDeclared(string field, string parameter)
    {
        if (field == null || !_fields.Contains(field, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{field}' is not a localizable field of {typeof(T).Name} (declared: {string.Join(", ", _fields)}).", parameter);
        }
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An entity id is required.", nameof(id));
        }
    }

    private static void RefuseLocalizedWrapper(object store)
    {
        for (object? current = store; current != null; current = (current as IStoreWrapper)?.GetInnerStore())
        {
            if (IsLocalizedWrapper(current.GetType()))
            {
                throw new ArgumentException(
                    $"The entity store passed to {nameof(EntityTranslations<T, TTranslation>)} is, or wraps, a localized store "
                    + $"wrapper ({current.GetType().Name}). Pass the store it wraps (with DI, the AddEntityTranslations "
                    + "overload taking factories): the lookups read base values, and the wrapper would apply the ambient culture to them.", "entities");
            }
            if (current is not IStoreWrapper)
            {
                break;
            }
        }
    }

    private static bool IsLocalizedWrapper(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.IsGenericType && LocalizedWrappers.Contains(current.GetGenericTypeDefinition()))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary><see cref="EntityTranslations{T,TTranslation}"/> with the default translation model, <see cref="EntityTranslationModel"/>.</summary>
public class EntityTranslations<T> : EntityTranslations<T, EntityTranslationModel>
    where T : AbstractModel, ILocalizable
{
    public EntityTranslations(
        IAsyncBulkStore<T> entities,
        IAsyncBulkStore<EntityTranslationModel> translations,
        IEntityLocalizationContext context,
        ILocalizedWriteBoundary? boundary = null)
        : base(entities, translations, context, boundary)
    {
    }
}
