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
/// The one producer for writing an entity's translation rows, called by all four wrappers (TASK-168).
/// </summary>
/// <remarks>
/// <para>
/// Each wrapper used to carry its own copy of this loop, so SH-M072, SH-M073 and SH-M076 were each present
/// four times. The rules, stated once:
/// </para>
/// <list type="bullet">
/// <item><description><b>Rows are keyed by the GUID the caller passes</b>, which on a create is the one the
/// inner store <i>returned</i> — a store need not write it back onto the entity (SH-M076).</description></item>
/// <item><description><b><c>EntityType</c> is the store's type</b> (<c>typeof(T).Name</c>), because that is
/// what every filter queries; the runtime type of a subclass instance made its rows unfindable (SH-M072). A
/// row found under another name is rewritten.</description></item>
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
    /// field name — what a whole-entity write persists.
    /// </summary>
    public static Dictionary<string, string?> ValuesOf<T>(T entity) where T : class, ILocalizable
        => LocalizedEntityFields.Capture(entity, entity.GetLocalizableFields());

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
    /// field name; a field with no row is absent. Used to show a filter-based update action the same values a
    /// read would.
    /// </summary>
    public static Dictionary<string, string?> Read(
        IBulkStore<EntityTranslationModel> store, Guid entityGuid, string culture, IReadOnlyList<string> fields)
        => ToDictionary(store.Read(EntityTranslationFilter.ByEntityAndCulture(entityGuid, culture).ToExpression()), fields);

    /// <inheritdoc cref="Read"/>
    public static async Task<Dictionary<string, string?>> ReadAsync(
        IAsyncBulkStore<EntityTranslationModel> store, Guid entityGuid, string culture, IReadOnlyList<string> fields,
        CancellationToken ct)
        => ToDictionary(await store.ReadAsync(
            EntityTranslationFilter.ByEntityAndCulture(entityGuid, culture).ToExpression(), null, null, null, ct), fields);

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
            var existing = store.Read(
                EntityTranslationFilter.ByEntityFieldAndCulture(entityGuid, pair.Key, culture).ToExpression()).ToList();

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

            row.EntityType = entityType;
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
                EntityTranslationFilter.ByEntityFieldAndCulture(entityGuid, pair.Key, culture).ToExpression(),
                null, null, null, ct)).ToList();

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

            row.EntityType = entityType;
            row.Value = pair.Value;
            row.UpdatedAt = now;
            await store.UpdateAsync(row, ct: ct);
        }
    }

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
            if (fields.Contains(row.FieldName))
            {
                result[row.FieldName] = row.Value;
            }
        }
        return result;
    }
}
