using Birko.Data.Exceptions;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// The one producer for reading, writing and deleting an entity's translation rows, called by all four wrappers
/// (TASK-168, TASK-194, TASK-679), generic over the translation model a consumer supplies (TASK-680).
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
/// <item><description><b>A create the store refuses for its unique key is retried as an update</b> (SH-M074,
/// TASK-680): two writers saving one key at once leave one row, carrying the last value. The key itself is declared
/// by the store — <c>Birko.Data.Localization.SQL</c> maps it for SQL.</description></item>
/// <item><description><b>Filters are built on the concrete translation type</b> (<see cref="Where{TTranslation}"/>),
/// never through <see cref="IEntityTranslation"/>. A precaution, not a measured failure: a member access through the
/// interface was not tried against every provider's translator, and the concrete property is what each of them maps.</description></item>
/// </list>
/// </remarks>
internal static class LocalizedTranslationRows
{
    /// <summary>
    /// The <c>EntityType</c> value for a store of <typeparamref name="T"/>, used by both the rows written here and
    /// every filter that reads them back (rule 16: one producer for a name written and read).
    /// </summary>
    public static string EntityTypeOf<T>() => typeof(T).Name;

    /// <summary>
    /// How many GUIDs one batched read puts in its <c>IN</c> list. Kept well under the smallest provider limit
    /// (MSSQL's ~2100 parameters), because the in-memory-sort path localizes a whole matching set at once.
    /// </summary>
    public const int ReadBatchSize = 500;

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
    /// A filter over <typeparamref name="TTranslation"/> on the parts of the key that are given, built with
    /// <see cref="Expression.Property(Expression, string)"/> on the concrete type and constants for the values.
    /// </summary>
    /// <exception cref="ArgumentException">No part is given: the filter would match every row of every store.</exception>
    public static Expression<Func<TTranslation, bool>> Where<TTranslation>(
        Guid? entityGuid = null,
        IReadOnlyCollection<Guid>? entityGuids = null,
        string? entityType = null,
        string? fieldName = null,
        string? culture = null)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var row = Expression.Parameter(typeof(TTranslation), "t");
        var terms = new List<Expression>();
        if (entityGuid is Guid guid)
        {
            terms.Add(Expression.Equal(Expression.Property(row, nameof(IEntityTranslation.EntityGuid)), Expression.Constant(guid)));
        }
        if (entityGuids != null)
        {
            var list = entityGuids.ToList();
            var contains = typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains), new[] { typeof(Guid) })!;
            terms.Add(Expression.Call(Expression.Constant(list), contains,
                Expression.Property(row, nameof(IEntityTranslation.EntityGuid))));
        }
        AddString(terms, row, nameof(IEntityTranslation.EntityType), entityType);
        AddString(terms, row, nameof(IEntityTranslation.FieldName), fieldName);
        AddString(terms, row, nameof(IEntityTranslation.Culture), culture);
        if (terms.Count == 0)
        {
            throw new ArgumentException("A translation filter needs at least one part of the key.");
        }

        return Expression.Lambda<Func<TTranslation, bool>>(terms.Aggregate(Expression.AndAlso), row);
    }

    private static void AddString(List<Expression> terms, ParameterExpression row, string property, string? value)
    {
        if (value == null)
        {
            return;
        }
        terms.Add(Expression.Equal(Expression.Property(row, property), Expression.Constant(value)));
    }

    /// <summary>
    /// <paramref name="culture"/>'s translation of each of <paramref name="fields"/> for one entity, keyed by
    /// field name; a field with no row is absent. What a filter-based update action sees.
    /// </summary>
    public static Dictionary<string, string?> Read<TTranslation>(
        IBulkStore<TTranslation> store, Guid entityGuid, string entityType, string culture, IReadOnlyList<string> fields)
        where TTranslation : AbstractModel, IEntityTranslation
        => ToDictionary(store.Read(Where<TTranslation>(entityGuid, entityType: entityType, culture: culture)), fields);

    /// <inheritdoc cref="Read"/>
    public static async Task<Dictionary<string, string?>> ReadAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, Guid entityGuid, string entityType, string culture,
        IReadOnlyList<string> fields, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
        => ToDictionary(await store.ReadAsync(
            Where<TTranslation>(entityGuid, entityType: entityType, culture: culture), null, null, null, ct), fields);

    /// <summary>
    /// <paramref name="culture"/>'s translations of many entities, grouped <c>EntityGuid → (FieldName → Value)</c>,
    /// in one read per <see cref="ReadBatchSize"/> GUIDs instead of one per entity (TASK-679). An entity with no rows
    /// is absent from the result.
    /// </summary>
    public static Dictionary<Guid, Dictionary<string, string?>> ReadMany<TTranslation>(
        IBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType, string culture)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var result = new Dictionary<Guid, Dictionary<string, string?>>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            Group(result, store.Read(Where<TTranslation>(entityGuids: chunk, entityType: entityType, culture: culture)));
        }
        return result;
    }

    /// <inheritdoc cref="ReadMany"/>
    public static async Task<Dictionary<Guid, Dictionary<string, string?>>> ReadManyAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType, string culture,
        CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var result = new Dictionary<Guid, Dictionary<string, string?>>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            Group(result, await store.ReadAsync(
                Where<TTranslation>(entityGuids: chunk, entityType: entityType, culture: culture), null, null, null, ct));
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
        where T : AbstractModel, ILocalizable
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

    public static void Save<TTranslation>(
        IBulkStore<TTranslation> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var now = DateTime.UtcNow;
        foreach (var pair in values)
        {
            var key = Where<TTranslation>(entityGuid, entityType: entityType, fieldName: pair.Key, culture: culture);
            var existing = store.Read(key).ToList();

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
                try
                {
                    store.Create(Fill(store.CreateInstance(), entityGuid, entityType, pair.Key, culture, pair.Value, now));
                    continue;
                }
                catch (StoreUniqueViolationException violation)
                {
                    // SH-M074: another writer created this key between our read and our create. Its row is the one to
                    // update; when there is none the refusal was about something else, so it stands.
                    row = ReRead(() => store.Read(key).FirstOrDefault(), violation);
                }
            }

            row.Value = pair.Value;
            row.UpdatedAt = now;
            store.Update(row);
        }
    }

    /// <inheritdoc cref="Save"/>
    public static async Task SaveAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var now = DateTime.UtcNow;
        foreach (var pair in values)
        {
            var key = Where<TTranslation>(entityGuid, entityType: entityType, fieldName: pair.Key, culture: culture);
            var existing = (await store.ReadAsync(key, null, null, null, ct)).ToList();

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
                try
                {
                    await store.CreateAsync(Fill(store.CreateInstance(), entityGuid, entityType, pair.Key, culture, pair.Value, now), ct: ct);
                    continue;
                }
                catch (StoreUniqueViolationException violation)
                {
                    // SH-M074: see Save.
                    TTranslation? raced;
                    try
                    {
                        raced = (await store.ReadAsync(key, null, null, null, ct)).FirstOrDefault();
                    }
                    catch (Exception readFailure) when (readFailure is not OperationCanceledException)
                    {
                        throw Unretryable(violation, readFailure);
                    }
                    row = raced ?? throw Unretryable(violation, null);
                }
            }

            row.Value = pair.Value;
            row.UpdatedAt = now;
            await store.UpdateAsync(row, ct: ct);
        }
    }

    /// <summary>
    /// The re-read after a create the unique key refused. When it finds no row, or cannot read at all, the original
    /// refusal is what the caller gets.
    /// </summary>
    /// <remarks>
    /// Measured on PostgreSQL 16 inside a caller's <c>SqlUnitOfWork</c>: the refused INSERT aborts the transaction, so
    /// the re-read fails with <c>25P02 current transaction is aborted</c> — which hid the unique violation that caused it.
    /// The caller's transaction is lost either way there; the error now says why. SQL Server keeps the transaction and
    /// the retry succeeds inside it (measured).
    /// </remarks>
    private static TTranslation ReRead<TTranslation>(Func<TTranslation?> read, StoreUniqueViolationException violation)
        where TTranslation : class
    {
        TTranslation? raced;
        try
        {
            raced = read();
        }
        catch (Exception readFailure) when (readFailure is not OperationCanceledException)
        {
            throw Unretryable(violation, readFailure);
        }
        return raced ?? throw Unretryable(violation, null);
    }

    private static Exception Unretryable(StoreUniqueViolationException violation, Exception? readFailure)
    {
        if (readFailure == null)
        {
            // No row holds the key: the refusal was about something else, so it stands as it was thrown.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(violation).Throw();
        }
        return new StoreUniqueViolationException(
            $"{violation.Message} The upsert could not re-read the key to update it instead (the transaction may have been "
            + "aborted by the refusal, as PostgreSQL does): " + readFailure!.Message, violation);
    }

    /// <summary>Deletes every row of one entity, in every culture.</summary>
    public static void Delete<TTranslation>(IBulkStore<TTranslation> store, Guid entityGuid, string entityType)
        where TTranslation : AbstractModel, IEntityTranslation
        => store.Delete(store.Read(Where<TTranslation>(entityGuid, entityType: entityType)).ToList());

    /// <inheritdoc cref="Delete"/>
    public static async Task DeleteAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, Guid entityGuid, string entityType, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
        => await store.DeleteAsync(
            (await store.ReadAsync(Where<TTranslation>(entityGuid, entityType: entityType), null, null, null, ct)).ToList(), ct);

    /// <summary>
    /// Deletes every row of one entity type — what destroying that type's store leaves behind (SH-L078). Scoped to
    /// the type, because the translation store is shared by every localized store.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="entityType"/> is null or empty: an unscoped delete would remove every row of every store
    /// (rules 2 and 5).
    /// </exception>
    public static void DeleteType<TTranslation>(IBulkStore<TTranslation> store, string entityType)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        store.Delete(Where<TTranslation>(entityType: entityType));
    }

    /// <inheritdoc cref="DeleteType"/>
    public static Task DeleteTypeAsync<TTranslation>(IAsyncBulkStore<TTranslation> store, string entityType, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        return store.DeleteAsync(Where<TTranslation>(entityType: entityType), ct);
    }

    /// <summary>
    /// Fills a row the store created (<c>CreateInstance</c>), so the model needs no parameterless-constructor constraint
    /// — a model with <c>required</c> members could not satisfy one.
    /// </summary>
    private static TTranslation Fill<TTranslation>(
        TTranslation row, Guid entityGuid, string entityType, string fieldName, string culture, string value, DateTime now)
        where TTranslation : IEntityTranslation
    {
        row.EntityGuid = entityGuid;
        row.EntityType = entityType;
        row.FieldName = fieldName;
        row.Culture = culture;
        row.Value = value;
        row.UpdatedAt = now;
        return row;
    }

    /// <summary>
    /// Refuses a translation model whose <see cref="IEntityTranslation"/> members are not public instance properties
    /// under those names — an explicit interface implementation, for instance. Every filter here is built with
    /// <c>Expression.Property</c> on the model by name, so such a model compiled and then failed at its first query.
    /// </summary>
    /// <exception cref="InvalidOperationException">A member is missing or has another type.</exception>
    public static void RequireQueryableModel<TTranslation>() where TTranslation : IEntityTranslation
    {
        foreach (var member in typeof(IEntityTranslation).GetProperties())
        {
            var prop = typeof(TTranslation).GetProperty(member.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop == null || prop.PropertyType != member.PropertyType)
            {
                throw new InvalidOperationException(
                    $"{typeof(TTranslation).Name} must expose {nameof(IEntityTranslation)}.{member.Name} as a public "
                    + $"{member.PropertyType.Name} property under that name (not an explicit interface implementation): "
                    + "translation filters address it by name.");
            }
        }
    }

    private static void Group<TTranslation>(Dictionary<Guid, Dictionary<string, string?>> result, IEnumerable<TTranslation> rows)
        where TTranslation : IEntityTranslation
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

    private static Dictionary<string, string?> ToDictionary<TTranslation>(IEnumerable<TTranslation> rows, IReadOnlyList<string> fields)
        where TTranslation : IEntityTranslation
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
