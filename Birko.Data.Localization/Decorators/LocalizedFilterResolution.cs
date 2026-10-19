using Birko.Data.Localization.Expressions;
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
/// The one producer of a filter rewritten for a non-default culture, for all four wrappers (TASK-681): each condition on a
/// translated field becomes <c>guid ∈ T ∨ (Part ∧ guid ∉ H)</c>, ANDed with the rest of the filter.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>T</b> — the entities whose translation matches. For <c>==</c> and <c>IN</c> the translation query
/// selects rows by <see cref="IEntityTranslation.ValueHash"/> and the value is confirmed in memory; every other shape
/// reads the field's translations in that culture and tests them in memory (SH-M082 remains for those).</description></item>
/// <item><description><b>Part ∧ guid ∉ H</b> — the fallback (SH-M070): an entity with no translation for the field in
/// that culture is matched on its base column. <b>H</b> is which of the base-column candidates have such a translation —
/// the candidates read from the entity store with <c>remaining ∧ Part</c>, then one batched translation read over them.
/// An entity store that cannot list (a plain <see cref="IStore{T}"/>) gives no candidates, so H is every entity with a
/// translation for the field — correct, not cheaper.</description></item>
/// <item><description>An empty set is reduced away, never rendered as a constant term (rule 3).</description></item>
/// <item><description>A translation row with no stored hash is still found by an exact-value lookup: the lookup also reads
/// the field's unhashed rows and confirms each by its value — correct, and cheap again once rows are backfilled.</description></item>
/// <item><description>Two limits, recorded: the base-column part is evaluated under the entity store's collation (case-
/// insensitive on SQL Server and MySQL by default) while translations match exactly; and a translation created between
/// the candidate read and the query can let that entity match on its base value once.</description></item>
/// </list>
/// The matched GUIDs still reach the entity store as <c>IN</c> lists (SH-L087, TASK-686).
/// </remarks>
internal static class LocalizedFilterResolution
{
    public static Expression<Func<T, bool>>? Rewrite<T, TTranslation>(
        Expression<Func<T, bool>>? filter,
        IReadOnlyList<string> localizableFields,
        IBulkStore<TTranslation> translations,
        IBulkStore<T>? entities,
        string culture)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var split = LocalizedExpressionAnalyzer.Split(filter, localizableFields);
        if (!split.HasLocalizedConditions)
        {
            return filter;
        }

        var param = filter!.Parameters[0];
        var entityType = LocalizedTranslationRows.EntityTypeOf<T>();
        var terms = new List<Expression>();
        if (split.RemainingFilter != null)
        {
            terms.Add(split.RemainingFilter.Body);
        }

        foreach (var condition in split.LocalizedConditions)
        {
            var slice = condition.Values == null || entities == null
                ? translations.Read(LocalizedTranslationRows.Where<TTranslation>(
                    entityType: entityType, fieldName: condition.FieldName, culture: culture)).ToList()
                : null;
            // With a slice already read (no bulk entity store), matching it saves the hash lookups. Without one,
            // Values is set: the slice is read whenever it is null.
            var matched = slice != null
                ? Matching(slice, condition)
                : Matching(ByHash(condition.Values!).SelectMany(hashes => translations.Read(LocalizedTranslationRows.Where<TTranslation>(
                    entityType: entityType, fieldName: condition.FieldName, culture: culture, valueHashes: hashes))), condition);
            var translated = entities == null
                ? slice!.Select(t => t.EntityGuid).ToHashSet()
                : Translated(translations, Candidates(entities, split.RemainingFilter, condition, param), entityType, condition.FieldName, culture);

            terms.Add(Term(param, matched, condition.Part, translated));
        }

        return Expression.Lambda<Func<T, bool>>(terms.Aggregate(Expression.AndAlso), param);
    }

    public static async Task<Expression<Func<T, bool>>?> RewriteAsync<T, TTranslation>(
        Expression<Func<T, bool>>? filter,
        IReadOnlyList<string> localizableFields,
        IAsyncBulkStore<TTranslation> translations,
        IAsyncBulkStore<T>? entities,
        string culture,
        CancellationToken ct)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var split = LocalizedExpressionAnalyzer.Split(filter, localizableFields);
        if (!split.HasLocalizedConditions)
        {
            return filter;
        }

        var param = filter!.Parameters[0];
        var entityType = LocalizedTranslationRows.EntityTypeOf<T>();
        var terms = new List<Expression>();
        if (split.RemainingFilter != null)
        {
            terms.Add(split.RemainingFilter.Body);
        }

        foreach (var condition in split.LocalizedConditions)
        {
            List<TTranslation>? slice = null;
            if (condition.Values == null || entities == null)
            {
                slice = (await translations.ReadAsync(LocalizedTranslationRows.Where<TTranslation>(
                    entityType: entityType, fieldName: condition.FieldName, culture: culture), null, null, null, ct)).ToList();
            }

            HashSet<Guid> matched;
            if (slice != null)
            {
                matched = Matching(slice, condition);
            }
            else
            {
                var rows = new List<TTranslation>();
                foreach (var hashes in ByHash(condition.Values!))
                {
                    rows.AddRange(await translations.ReadAsync(LocalizedTranslationRows.Where<TTranslation>(
                        entityType: entityType, fieldName: condition.FieldName, culture: culture, valueHashes: hashes), null, null, null, ct));
                }
                matched = Matching(rows, condition);
            }

            HashSet<Guid> translated;
            if (entities == null)
            {
                translated = slice!.Select(t => t.EntityGuid).ToHashSet();
            }
            else
            {
                var candidates = (await entities.ReadAsync(BaseFilter(split.RemainingFilter, condition, param), null, null, null, ct))
                    .Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value).ToList();
                translated = new HashSet<Guid>();
                foreach (var chunk in candidates.Distinct().Chunk(LocalizedTranslationRows.ReadBatchSize))
                {
                    translated.UnionWith((await translations.ReadAsync(LocalizedTranslationRows.Where<TTranslation>(
                        entityGuids: chunk, entityType: entityType, fieldName: condition.FieldName, culture: culture), null, null, null, ct))
                        .Select(t => t.EntityGuid));
                }
            }

            terms.Add(Term(param, matched, condition.Part, translated));
        }

        return Expression.Lambda<Func<T, bool>>(terms.Aggregate(Expression.AndAlso), param);
    }

    /// <summary>The value hashes of an <c>==</c> / <c>IN</c> condition, in batches that keep each IN list bounded.</summary>
    private static IEnumerable<string[]> ByHash(IReadOnlyList<string> values)
        => values.Select(LocalizedValueHash.Of).Distinct(StringComparer.Ordinal).Chunk(LocalizedTranslationRows.ReadBatchSize);

    /// <summary>The entities whose row passes the condition — the value confirmed in memory, also behind a hash lookup.</summary>
    private static HashSet<Guid> Matching<TTranslation>(IEnumerable<TTranslation> rows, LocalizedFieldCondition condition)
        where TTranslation : IEntityTranslation
        => rows.Where(t => condition.ValuePredicate(t.Value)).Select(t => t.EntityGuid).ToHashSet();

    private static Expression<Func<T, bool>> BaseFilter<T>(
        Expression<Func<T, bool>>? remaining, LocalizedFieldCondition condition, ParameterExpression param)
        => Expression.Lambda<Func<T, bool>>(
            remaining == null ? condition.Part : Expression.AndAlso(remaining.Body, condition.Part), param);

    private static List<Guid> Candidates<T>(
        IBulkStore<T> entities, Expression<Func<T, bool>>? remaining, LocalizedFieldCondition condition, ParameterExpression param)
        where T : AbstractModel
        => entities.Read(BaseFilter(remaining, condition, param), null, null, null)
            .Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value).ToList();

    /// <summary>Which of <paramref name="candidates"/> have a translation for the field in the culture — one read per batch.</summary>
    private static HashSet<Guid> Translated<TTranslation>(
        IBulkStore<TTranslation> translations, IEnumerable<Guid> candidates, string entityType, string fieldName, string culture)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        var result = new HashSet<Guid>();
        foreach (var chunk in candidates.Distinct().Chunk(LocalizedTranslationRows.ReadBatchSize))
        {
            result.UnionWith(translations.Read(LocalizedTranslationRows.Where<TTranslation>(
                entityGuids: chunk, entityType: entityType, fieldName: fieldName, culture: culture)).Select(t => t.EntityGuid));
        }
        return result;
    }

    /// <summary><c>guid ∈ matched ∨ (part ∧ guid ∉ translated)</c>, with each empty set reduced away (rule 3).</summary>
    private static Expression Term(ParameterExpression param, HashSet<Guid> matched, Expression part, HashSet<Guid> translated)
    {
        var fallback = translated.Count == 0 ? part : Expression.AndAlso(part, Expression.Not(InSet(param, translated)));
        return matched.Count == 0 ? fallback : Expression.OrElse(InSet(param, matched), fallback);
    }

    /// <summary><c>x.Guid != null &amp;&amp; guids.Contains(x.Guid.Value)</c> over a <see cref="List{T}"/> constant.</summary>
    private static Expression InSet(ParameterExpression param, HashSet<Guid> guids)
    {
        var guid = Expression.Property(param, nameof(AbstractModel.Guid));
        var contains = typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains), new[] { typeof(Guid) })!;
        return Expression.AndAlso(
            Expression.NotEqual(guid, Expression.Constant(null, typeof(Guid?))),
            Expression.Call(Expression.Constant(guids.ToList()), contains, Expression.Property(guid, nameof(Nullable<Guid>.Value))));
    }
}
