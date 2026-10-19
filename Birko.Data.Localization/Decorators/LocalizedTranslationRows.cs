using Birko.Data.Localization.Filters;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// The one producer for reading, writing and deleting an entity's translation rows, called by all four wrappers
/// (TASK-168, TASK-194).
/// </summary>
/// <remarks>
/// <para>
/// Each wrapper used to carry its own copy of these loops, so SH-M072, SH-M073 and SH-M076 were each present
/// four times. The rules, stated once:
/// </para>
/// <list type="bullet">
/// <item><description><b>A row is identified by the four-part key</b> <c>(EntityGuid, EntityType, FieldName,
/// Culture)</c> the model documents: every read, upsert and delete here filters on all of the parts it knows, the
/// entity type included (SH-L080). A row stored under another type name belongs to another store.</description></item>
/// <item><description><b>Rows are keyed by the GUID the caller passes</b>, which on a create is the one the
/// inner store <i>returned</i> — a store need not write it back onto the entity (SH-M076).</description></item>
/// <item><description><b><c>EntityType</c> is the store's type</b> (<see cref="EntityTypeOf{T}"/>), because that is
/// what every filter queries; the runtime type of a subclass instance made its rows unfindable (SH-M072).</description></item>
/// <item><description><b>A <c>null</c> value removes the row</b>, so the read falls back to the base value;
/// skipping it left the old translation to be re-applied on the next read (SH-M073). An empty string is a
/// value and is stored.</description></item>
/// </list>
/// <para>
/// Duplicate rows for one key (SH-M074) are not prevented here; that needs the unique key TASK-680 adds.
/// </para>
/// </remarks>
internal static class LocalizedTranslationRows
{
    /// <summary>
    /// The <c>EntityType</c> value for a store of <typeparamref name="T"/>, used by both the rows written here and
    /// every filter that reads them back (rule 16: one producer for a name written and read).
    /// </summary>
    public static string EntityTypeOf<T>() => typeof(T).Name;

    /// <summary>
    /// The values of every localizable <see cref="string"/> property of <paramref name="entity"/>, keyed by
    /// field name — what a whole-entity write persists. The declaration is validated (SH-L079).
    /// </summary>
    public static Dictionary<string, string?> ValuesOf<T>(T entity) where T : class, ILocalizable
        => LocalizedEntityFields.Capture(entity, LocalizedEntityFields.LocalizableFieldsOf(entity));

    /// <summary>
    /// The fields whose value differs between <paramref name="before"/> and <paramref name="after"/>, with
    /// the <paramref name="after"/> value — what a filter-based update action actually changed.
    /// </summary>
    public static Dictionary<string, string?> Changed(
        IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after)
    {
        var changed = new Dictionary<string, string?>();
        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out var old) || !string.Equals(old, pair.Value, StringComparison.Ordinal))
            {
                changed[pair.Key] = pair.Value;
            }
        }
        return changed;
    }

    /// <summary>
    /// <paramref name="culture"/>'s translation of each of <paramref name="fields"/> for one entity, keyed by
    /// field name; a field with no row is absent. What a read applies, and what a filter-based update action sees.
    /// </summary>
    public static Dictionary<string, string?> Read(
        IBulkStore<EntityTranslationModel> store, Guid entityGuid, string entityType, string culture, IReadOnlyList<string> fields)
        => ToDictionary(store.Read(Key(entityGuid, entityType, culture).ToExpression()), fields);

    /// <inheritdoc cref="Read"/>
    public static async Task<Dictionary<string, string?>> ReadAsync(
        IAsyncBulkStore<EntityTranslationModel> store, Guid entityGuid, string entityType, string culture,
        IReadOnlyList<string> fields, CancellationToken ct)
        => ToDictionary(await store.ReadAsync(
            Key(entityGuid, entityType, culture).ToExpression(), null, null, null, ct), fields);

    /// <summary>
    /// How many GUIDs one batched read puts in its <c>IN</c> list. Kept well under the smallest provider limit
    /// (MSSQL's ~2100 parameters), because the in-memory-sort path localizes a whole matching set at once.
    /// </summary>
    public const int ReadBatchSize = 500;

    /// <summary>
    /// <paramref name="culture"/>'s translations of many entities, grouped <c>EntityGuid → (FieldName → Value)</c>,
    /// in one read per <see cref="ReadBatchSize"/> GUIDs instead of one per entity (TASK-679). An entity with no rows
    /// is absent from the result.
    /// </summary>
    public static Dictionary<Guid, Dictionary<string, string?>> ReadMany(
        IBulkStore<EntityTranslationModel> store, IEnumerable<Guid> entityGuids, string entityType, string culture)
    {
        var result = new Dictionary<Guid, Dictionary<string, string?>>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            Group(result, store.Read(Many(chunk, entityType, culture)));
        }
        return result;
    }

    /// <inheritdoc cref="ReadMany"/>
    public static async Task<Dictionary<Guid, Dictionary<string, string?>>> ReadManyAsync(
        IAsyncBulkStore<EntityTranslationModel> store, IEnumerable<Guid> entityGuids, string entityType, string culture,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, Dictionary<string, string?>>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            Group(result, await store.ReadAsync(Many(chunk, entityType, culture), null, null, null, ct));
        }
        return result;
    }

    /// <summary>
    /// <paramref name="entity"/> as a batched read shows it: the same instance when it has no GUID, otherwise a detached
    /// copy (SH-H016) carrying its rows from <paramref name="rows"/> — only the fields its own declaration names — or its
    /// base values when it has none.
    /// </summary>
    /// <remarks>
    /// The one producer of "translation rows → a translated entity": the single-entity read, the list read and the
    /// in-memory sort all come through here, so a change to which fields apply cannot land in one path only.
    /// </remarks>
    public static T Apply<T>(T entity, IReadOnlyDictionary<Guid, Dictionary<string, string?>> rows)
        where T : Data.Models.AbstractModel, ILocalizable
    {
        if (entity.Guid is not Guid guid)
        {
            return entity;
        }

        var localized = LocalizedEntityFields.Detach(entity);
        // Validated whether or not rows exist, so a bad declaration is refused on every read, not only a translated one.
        var fields = LocalizedEntityFields.LocalizableFieldsOf(localized);
        if (rows.TryGetValue(guid, out var values))
        {
            LocalizedEntityFields.Restore(localized, OnlyFields(values, fields));
        }
        return localized;
    }

    /// <summary>
    /// <c>guids.Contains(t.EntityGuid) &amp;&amp; t.EntityType == type &amp;&amp; t.Culture == culture</c> over a captured
    /// <see cref="List{T}"/>: <c>List.Contains</c> is what the SQL, Elasticsearch and RavenDB translators turn into
    /// <c>IN</c> / terms, and a captured local is evaluated by each of them (and by the LINQ-based drivers).
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<EntityTranslationModel, bool>> Many(
        Guid[] chunk, string entityType, string culture)
    {
        var guids = chunk.ToList();
        return t => guids.Contains(t.EntityGuid) && t.EntityType == entityType && t.Culture == culture;
    }

    private static void Group(Dictionary<Guid, Dictionary<string, string?>> result, IEnumerable<EntityTranslationModel> rows)
    {
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.EntityGuid, out var fields))
            {
                fields = new Dictionary<string, string?>();
                result[row.EntityGuid] = fields;
            }
            fields[row.FieldName] = row.Value;
        }
    }

    public static void Save(
        IBulkStore<EntityTranslationModel> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in values)
        {
            var existing = store.Read(Key(entityGuid, entityType, culture, pair.Key).ToExpression()).ToList();

            if (pair.Value == null)
            {
                if (existing.Count > 0)
                {
                    store.Delete(existing);
                }
                continue;
            }

            var row = existing.FirstOrDefault();
            if (row == null)
            {
                store.Create(NewRow(entityGuid, entityType, pair.Key, culture, pair.Value, now));
                continue;
            }

            row.Value = pair.Value;
            row.UpdatedAt = now;
            store.Update(row);
        }
    }

    /// <inheritdoc cref="Save"/>
    public static async Task SaveAsync(
        IAsyncBulkStore<EntityTranslationModel> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in values)
        {
            var existing = (await store.ReadAsync(
                Key(entityGuid, entityType, culture, pair.Key).ToExpression(), null, null, null, ct)).ToList();

            if (pair.Value == null)
            {
                if (existing.Count > 0)
                {
                    await store.DeleteAsync(existing, ct);
                }
                continue;
            }

            var row = existing.FirstOrDefault();
            if (row == null)
            {
                await store.CreateAsync(NewRow(entityGuid, entityType, pair.Key, culture, pair.Value, now), ct: ct);
                continue;
            }

            row.Value = pair.Value;
            row.UpdatedAt = now;
            await store.UpdateAsync(row, ct: ct);
        }
    }

    /// <summary>Deletes every row of one entity, in every culture.</summary>
    public static void Delete(IBulkStore<EntityTranslationModel> store, Guid entityGuid, string entityType)
        => store.Delete(store.Read(Key(entityGuid, entityType).ToExpression()).ToList());

    /// <inheritdoc cref="Delete"/>
    public static async Task DeleteAsync(
        IAsyncBulkStore<EntityTranslationModel> store, Guid entityGuid, string entityType, CancellationToken ct)
        => await store.DeleteAsync(
            (await store.ReadAsync(Key(entityGuid, entityType).ToExpression(), null, null, null, ct)).ToList(), ct);

    /// <summary>
    /// Deletes every row of one entity type — what destroying that type's store leaves behind (SH-L078). Scoped to
    /// the type, because the translation store is shared by every localized store.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="entityType"/> is null or empty: the filter reads a null type as "any type", which would delete
    /// every row of every store (rules 2 and 5).
    /// </exception>
    public static void DeleteType(IBulkStore<EntityTranslationModel> store, string entityType)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        store.Delete(EntityTranslationFilter.ByEntityType(entityType).ToExpression());
    }

    /// <inheritdoc cref="DeleteType"/>
    public static Task DeleteTypeAsync(IAsyncBulkStore<EntityTranslationModel> store, string entityType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        return store.DeleteAsync(EntityTranslationFilter.ByEntityType(entityType).ToExpression(), ct);
    }

    private static EntityTranslationFilter Key(Guid entityGuid, string entityType, string? culture = null, string? fieldName = null)
        => new() { EntityGuid = entityGuid, EntityType = entityType, Culture = culture, FieldName = fieldName };

    private static EntityTranslationModel NewRow(
        Guid entityGuid, string entityType, string fieldName, string culture, string value, DateTime now) => new()
    {
        EntityGuid = entityGuid,
        EntityType = entityType,
        FieldName = fieldName,
        Culture = culture,
        Value = value,
        UpdatedAt = now,
    };

    private static Dictionary<string, string?> ToDictionary(IEnumerable<EntityTranslationModel> rows, IReadOnlyList<string> fields)
    {
        var result = new Dictionary<string, string?>();
        foreach (var row in rows)
        {
            result[row.FieldName] = row.Value;
        }
        return OnlyFields(result, fields);
    }

    /// <summary>The values whose field is one of <paramref name="fields"/> — the one place that selection is made.</summary>
    private static Dictionary<string, string?> OnlyFields(IReadOnlyDictionary<string, string?> values, IReadOnlyList<string> fields)
        => values.Where(pair => fields.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
}
