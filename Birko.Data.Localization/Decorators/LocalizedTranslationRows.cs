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
    /// <see cref="ValuesOf{T}"/> for every entity that has a GUID, as <see cref="SaveMany"/> takes them. Each declaration is
    /// validated, whatever the culture, as a single write validates it.
    /// </summary>
    public static List<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> ValuesOfAll<T>(IEnumerable<T> entities)
        where T : AbstractModel, ILocalizable
    {
        var result = new List<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)>();
        foreach (var entity in entities)
        {
            var values = ValuesOf(entity);
            if (entity.Guid is Guid guid)
            {
                result.Add((guid, values));
            }
        }
        return result;
    }

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
        string? culture = null,
        IReadOnlyCollection<string>? valueHashes = null)
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
        if (valueHashes != null)
        {
            // A row whose stored hash is missing (written before the column existed, or straight into the store by a
            // model that does not compute it) is read too and confirmed by its value in memory: otherwise its entity
            // would match neither on the translation nor, having one, on its base value (TASK-681 review).
            var hashes = valueHashes.ToList();
            var containsHash = typeof(List<string>).GetMethod(nameof(List<string>.Contains), new[] { typeof(string) })!;
            var stored = Expression.Property(row, nameof(IEntityTranslation.ValueHash));
            terms.Add(Expression.OrElse(
                Expression.Call(Expression.Constant(hashes), containsHash, stored),
                Expression.OrElse(
                    Expression.Equal(stored, Expression.Constant(null, typeof(string))),
                    Expression.Equal(stored, Expression.Constant(string.Empty)))));
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

    /// <summary>
    /// <paramref name="culture"/>'s translations of every entity in <paramref name="values"/>: a <c>null</c> value removes
    /// that field's row, anything else is upserted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-690: one read of the existing rows per <see cref="ReadBatchSize"/> entities, then at most one bulk create, one
    /// bulk update and one bulk delete, in that order, skipping any that has nothing to do.
    /// </para>
    /// <para>
    /// A bulk create the unique key refuses (SH-M074: another writer stored one of the keys after the read) re-reads the
    /// keys it was creating once: the rows another writer created are updated, and the rest are created again. When the
    /// re-read finds none of them, or the second create is refused too, the refusal was about something else and the
    /// original stands. A store that inserted part of the
    /// batch before refusing is covered too: its own rows are found and updated with the same values.
    /// </para>
    /// </remarks>
    public static void SaveMany<TTranslation>(
        IBulkStore<TTranslation> store,
        string entityType,
        string culture,
        IEnumerable<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> values)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var wanted = Wanted(values);
        if (wanted.Count == 0)
        {
            return;
        }

        var plan = PlanWrites(store.CreateInstance, ReadKeys(store, wanted.Keys, entityType, culture), wanted, entityType, culture);
        // Creates first: the one write a key can refuse. A refusal that stands at once (no key held, or the re-read
        // failed) then leaves nothing applied; one that stands after the retry leaves the retry's updates applied.
        if (plan.Creates.Count > 0)
        {
            try
            {
                store.Create(plan.Creates);
            }
            catch (StoreUniqueViolationException violation)
            {
                List<TTranslation> raced;
                try
                {
                    raced = ReadKeys(store, plan.Creates.Select(r => r.EntityGuid), entityType, culture);
                }
                catch (Exception readFailure) when (readFailure is not OperationCanceledException)
                {
                    throw Unretryable(violation, readFailure);
                }
                var retry = PlanRetry(plan.Creates, raced) ?? throw Unretryable(violation, null);
                store.Update(retry.UpdatedRows());
                if (retry.Creates.Count > 0)
                {
                    try
                    {
                        store.Create(retry.Creates);
                    }
                    catch (StoreUniqueViolationException)
                    {
                        // Refused again: the refusal was not (only) a race on these keys, so the original stands.
                        throw Unretryable(violation, null);
                    }
                }
            }
        }
        if (plan.UpdateCount > 0)
        {
            store.Update(plan.UpdatedRows());
        }
        if (plan.Deletes.Count > 0)
        {
            store.Delete(plan.Deletes);
        }
    }

    /// <inheritdoc cref="SaveMany"/>
    public static async Task SaveManyAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store,
        string entityType,
        string culture,
        IEnumerable<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> values,
        CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var wanted = Wanted(values);
        if (wanted.Count == 0)
        {
            return;
        }

        var plan = PlanWrites(store.CreateInstance, await ReadKeysAsync(store, wanted.Keys, entityType, culture, ct), wanted, entityType, culture);
        // Creates first: the one write a key can refuse. A refusal that stands at once (no key held, or the re-read
        // failed) then leaves nothing applied; one that stands after the retry leaves the retry's updates applied.
        if (plan.Creates.Count > 0)
        {
            try
            {
                await store.CreateAsync(plan.Creates, ct: ct);
            }
            catch (StoreUniqueViolationException violation)
            {
                List<TTranslation> raced;
                try
                {
                    raced = await ReadKeysAsync(store, plan.Creates.Select(r => r.EntityGuid), entityType, culture, ct);
                }
                catch (Exception readFailure) when (readFailure is not OperationCanceledException)
                {
                    throw Unretryable(violation, readFailure);
                }
                var retry = PlanRetry(plan.Creates, raced) ?? throw Unretryable(violation, null);
                await store.UpdateAsync(retry.UpdatedRows(), ct: ct);
                if (retry.Creates.Count > 0)
                {
                    try
                    {
                        await store.CreateAsync(retry.Creates, ct: ct);
                    }
                    catch (StoreUniqueViolationException)
                    {
                        // Refused again: the refusal was not (only) a race on these keys, so the original stands.
                        throw Unretryable(violation, null);
                    }
                }
            }
        }
        if (plan.UpdateCount > 0)
        {
            await store.UpdateAsync(plan.UpdatedRows(), ct: ct);
        }
        if (plan.Deletes.Count > 0)
        {
            await store.DeleteAsync(plan.Deletes, ct);
        }
    }

    /// <summary>One entity's translations: <see cref="SaveMany"/> for a single entity.</summary>
    public static void Save<TTranslation>(
        IBulkStore<TTranslation> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values)
        where TTranslation : AbstractModel, IEntityTranslation
        => SaveMany(store, entityType, culture, new[] { (entityGuid, values) });

    /// <inheritdoc cref="Save"/>
    public static Task SaveAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store,
        Guid entityGuid,
        string entityType,
        string culture,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
        => SaveManyAsync(store, entityType, culture, new[] { (entityGuid, values) }, ct);

    /// <summary>The requested values per entity, merged when an entity appears twice (the later value wins).</summary>
    private static Dictionary<Guid, Dictionary<string, string?>> Wanted(
        IEnumerable<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> values)
    {
        var wanted = new Dictionary<Guid, Dictionary<string, string?>>();
        foreach (var (entityGuid, fields) in values)
        {
            if (fields.Count == 0)
            {
                continue;
            }
            if (!wanted.TryGetValue(entityGuid, out var merged))
            {
                merged = new Dictionary<string, string?>();
                wanted[entityGuid] = merged;
            }
            foreach (var pair in fields)
            {
                merged[pair.Key] = pair.Value;
            }
        }
        return wanted;
    }

    /// <summary>Every row of these entities in <paramref name="culture"/>, one read per <see cref="ReadBatchSize"/> GUIDs.</summary>
    private static List<TTranslation> ReadKeys<TTranslation>(
        IBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType, string culture)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var rows = new List<TTranslation>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            rows.AddRange(store.Read(Where<TTranslation>(entityGuids: chunk, entityType: entityType, culture: culture)));
        }
        return rows;
    }

    /// <inheritdoc cref="ReadKeys"/>
    private static async Task<List<TTranslation>> ReadKeysAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType, string culture, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var rows = new List<TTranslation>();
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            rows.AddRange(await store.ReadAsync(
                Where<TTranslation>(entityGuids: chunk, entityType: entityType, culture: culture), null, null, null, ct));
        }
        return rows;
    }

    /// <summary>What one save writes. Updated rows receive their new value only when the update is sent.</summary>
    private sealed class WritePlan<TTranslation> where TTranslation : IEntityTranslation
    {
        private readonly List<(TTranslation Row, string Value, DateTime At)> _updates = new();

        public List<TTranslation> Deletes { get; } = new();
        public List<TTranslation> Creates { get; } = new();
        public int UpdateCount => _updates.Count;

        public void Update(TTranslation row, string value, DateTime at) => _updates.Add((row, value, at));

        /// <summary>
        /// The rows to update, with their new values set now. A row a store returned may be its own live instance (an
        /// in-memory store's is), so setting it while planning would apply the update even when the create sent first
        /// is refused.
        /// </summary>
        public List<TTranslation> UpdatedRows()
        {
            foreach (var (row, value, at) in _updates)
            {
                SetValue(row, value, at);
            }
            return _updates.Select(u => u.Row).ToList();
        }
    }

    /// <summary>
    /// Splits the requested values into rows to delete, update and create: the one place that decision is made, for the
    /// sync and async saves alike. A <c>null</c> value deletes every row of its key; a value updates the key's first row.
    /// </summary>
    private static WritePlan<TTranslation> PlanWrites<TTranslation>(
        Func<TTranslation> createInstance,
        List<TTranslation> existing,
        Dictionary<Guid, Dictionary<string, string?>> wanted,
        string entityType,
        string culture)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var byKey = existing.ToLookup(r => (r.EntityGuid, r.FieldName));
        var plan = new WritePlan<TTranslation>();
        var now = DateTime.UtcNow;
        foreach (var (entityGuid, fields) in wanted)
        {
            foreach (var (field, value) in fields)
            {
                var rows = byKey[(entityGuid, field)];
                if (value == null)
                {
                    plan.Deletes.AddRange(rows);
                    continue;
                }

                var row = rows.FirstOrDefault();
                if (row == null)
                {
                    plan.Creates.Add(Fill(createInstance(), entityGuid, entityType, field, culture, value, now));
                    continue;
                }
                plan.Update(row, value, now);
            }
        }
        return plan;
    }

    /// <summary>
    /// After a refused create: each refused row whose key another writer now holds becomes an update of that writer's
    /// row, and the rest are created again. <c>null</c> when no key is held: the refusal was about something else.
    /// </summary>
    private static WritePlan<TTranslation>? PlanRetry<TTranslation>(List<TTranslation> refused, List<TTranslation> raced)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var byKey = raced.ToLookup(r => (r.EntityGuid, r.FieldName));
        var retry = new WritePlan<TTranslation>();
        foreach (var row in refused)
        {
            var held = byKey[(row.EntityGuid, row.FieldName)].FirstOrDefault();
            if (held == null)
            {
                retry.Creates.Add(row);
                continue;
            }
            retry.Update(held, row.Value, row.UpdatedAt ?? DateTime.UtcNow);
        }
        return retry.UpdateCount == 0 ? null : retry;
    }

    private static void SetValue<TTranslation>(TTranslation row, string value, DateTime now)
        where TTranslation : IEntityTranslation
    {
        row.Value = value;
        row.ValueHash = LocalizedValueHash.Of(value);
        row.UpdatedAt = now;
    }

    /// <summary>
    /// What a refused create throws when its re-read cannot run, or finds none of the keys it was creating held: the
    /// original refusal, with the read failure appended when there is one.
    /// </summary>
    /// <remarks>
    /// Measured on PostgreSQL 16 inside a caller's <c>SqlUnitOfWork</c>: the refused INSERT aborts the transaction, so
    /// the re-read fails with <c>25P02 current transaction is aborted</c> — which hid the unique violation that caused it.
    /// The caller's transaction is lost either way there; the error now says why. SQL Server keeps the transaction and
    /// the retry succeeds inside it (measured).
    /// </remarks>
    private static Exception Unretryable(StoreUniqueViolationException violation, Exception? readFailure)
        // No read failure: no row holds the key, so the refusal was about something else and stands as it was thrown.
        => StoreException.UnretryableUniqueViolation(violation, readFailure);

    /// <summary>Deletes every row of one entity, in every culture: <see cref="DeleteMany"/> for a single entity.</summary>
    public static void Delete<TTranslation>(IBulkStore<TTranslation> store, Guid entityGuid, string entityType)
        where TTranslation : AbstractModel, IEntityTranslation
        => DeleteMany(store, new[] { entityGuid }, entityType);

    /// <inheritdoc cref="Delete"/>
    public static Task DeleteAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, Guid entityGuid, string entityType, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
        => DeleteManyAsync(store, new[] { entityGuid }, entityType, ct);

    /// <summary>
    /// Deletes every row of these entities, in every culture, as one filter delete per <see cref="ReadBatchSize"/> GUIDs
    /// (TASK-690).
    /// </summary>
    /// <remarks>
    /// An empty list makes no call at all. A filter delete with an empty <c>IN</c> list means "nothing" in LINQ but is
    /// rendered differently per provider, and the scope guard refuses an empty filter (rules 2, 3 and 5), so the
    /// question is never put to the store.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="entityType"/> is null or empty (rules 2 and 5).</exception>
    public static void DeleteMany<TTranslation>(IBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            store.Delete(Where<TTranslation>(entityGuids: chunk, entityType: entityType));
        }
    }

    /// <inheritdoc cref="DeleteMany"/>
    public static async Task DeleteManyAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, IEnumerable<Guid> entityGuids, string entityType, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        foreach (var chunk in entityGuids.Distinct().Chunk(ReadBatchSize))
        {
            await store.DeleteAsync(Where<TTranslation>(entityGuids: chunk, entityType: entityType), ct);
        }
    }

    /// <summary>Deletes one entity's rows in one culture, as one filter delete; its other cultures are left alone.</summary>
    /// <exception cref="ArgumentException"><paramref name="entityType"/> or <paramref name="culture"/> is null or empty.</exception>
    public static void DeleteCulture<TTranslation>(IBulkStore<TTranslation> store, Guid entityGuid, string entityType, string culture)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        ArgumentException.ThrowIfNullOrEmpty(culture);
        store.Delete(Where<TTranslation>(entityGuid, entityType: entityType, culture: culture));
    }

    /// <inheritdoc cref="DeleteCulture"/>
    public static Task DeleteCultureAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, Guid entityGuid, string entityType, string culture, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        ArgumentException.ThrowIfNullOrEmpty(culture);
        return store.DeleteAsync(Where<TTranslation>(entityGuid, entityType: entityType, culture: culture), ct);
    }

    /// <summary>
    /// Every culture's translations of one entity, grouped <c>Culture → (FieldName → Value)</c>, in one read. A culture
    /// with no rows is absent.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string?>> ReadAllCultures<TTranslation>(
        IBulkStore<TTranslation> store, Guid entityGuid, string entityType)
        where TTranslation : AbstractModel, IEntityTranslation
        => GroupByCulture(store.Read(Where<TTranslation>(entityGuid, entityType: entityType)));

    /// <inheritdoc cref="ReadAllCultures"/>
    public static async Task<Dictionary<string, Dictionary<string, string?>>> ReadAllCulturesAsync<TTranslation>(
        IAsyncBulkStore<TTranslation> store, Guid entityGuid, string entityType, CancellationToken ct)
        where TTranslation : AbstractModel, IEntityTranslation
        => GroupByCulture(await store.ReadAsync(Where<TTranslation>(entityGuid, entityType: entityType), null, null, null, ct));

    private static Dictionary<string, Dictionary<string, string?>> GroupByCulture<TTranslation>(IEnumerable<TTranslation> rows)
        where TTranslation : IEntityTranslation
    {
        var result = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.Culture, out var fields))
            {
                fields = new Dictionary<string, string?>();
                result[row.Culture] = fields;
            }
            fields[row.FieldName] = row.Value;
        }
        return result;
    }

    /// <summary>
    /// <paramref name="entities"/> in <paramref name="culture"/>, each replaced by <see cref="Apply{T}"/>'s result, from one
    /// translation read per <see cref="ReadBatchSize"/> entities: the one localize path for a single read, a list read
    /// and the in-memory sort (TASK-679, TASK-690). The list is updated in place and returned.
    /// </summary>
    public static List<T> LocalizeAll<T, TTranslation>(IBulkStore<TTranslation> store, List<T> entities, string culture)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
    {
        if (entities.Count == 0)
        {
            return entities;
        }
        return ApplyAll(entities, ReadMany(store, GuidsOf(entities), EntityTypeOf<T>(), culture));
    }

    /// <inheritdoc cref="LocalizeAll"/>
    public static async Task<List<T>> LocalizeAllAsync<T, TTranslation>(
        IAsyncBulkStore<TTranslation> store, List<T> entities, string culture, CancellationToken ct)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
    {
        if (entities.Count == 0)
        {
            return entities;
        }
        return ApplyAll(entities, await ReadManyAsync(store, GuidsOf(entities), EntityTypeOf<T>(), culture, ct));
    }

    /// <summary>The GUIDs of the entities that have one.</summary>
    public static IEnumerable<Guid> GuidsOf<T>(IEnumerable<T> entities) where T : AbstractModel
        => entities.Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value);

    private static List<T> ApplyAll<T>(List<T> entities, IReadOnlyDictionary<Guid, Dictionary<string, string?>> rows)
        where T : AbstractModel, ILocalizable
    {
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i] = Apply(entities[i], rows);
        }
        return entities;
    }

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
        row.ValueHash = LocalizedValueHash.Of(value);
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

    /// <summary>The values whose field is one of <paramref name="fields"/> — the one place that selection is made.</summary>
    public static Dictionary<string, string?> OnlyFields(IReadOnlyDictionary<string, string?> values, IReadOnlyList<string> fields)
        => values.Where(pair => fields.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
}
