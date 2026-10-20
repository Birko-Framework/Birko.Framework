using Birko.Data.Localization.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// A filter rewritten for a culture, as the entity store will run it (TASK-686): one query, or N <b>disjoint</b> chunked
/// queries whose results together are the match, or none at all.
/// </summary>
/// <remarks>
/// Chunked queries come from <see cref="LocalizedFilterResolution"/> when the matched GUID sets outgrow one batch: each
/// chunk is <c>remaining ∧ guid ∈ chunk</c> over a partition of one set, so no row is in two chunks. That is what makes
/// concatenation and summed counts exact. An empty chunk list is a match of nothing, never the remaining filter alone
/// (rules 2 and 3).
/// </remarks>
internal sealed class LocalizedQueries<T> where T : AbstractModel
{
    private readonly Expression<Func<T, bool>>? _single;
    private readonly IReadOnlyList<Expression<Func<T, bool>>>? _chunks;

    private LocalizedQueries(Expression<Func<T, bool>>? single, IReadOnlyList<Expression<Func<T, bool>>>? chunks)
    {
        _single = single;
        _chunks = chunks;
    }

    public static LocalizedQueries<T> One(Expression<Func<T, bool>>? filter) => new(filter, null);

    public static LocalizedQueries<T> Chunked(IReadOnlyList<Expression<Func<T, bool>>> chunks) => new(null, chunks);

    public bool IsChunked => _chunks != null;

    /// <summary>The one query (null: no filter).</summary>
    /// <exception cref="InvalidOperationException">The queries are chunked.</exception>
    public Expression<Func<T, bool>>? Single
        => IsChunked ? throw new InvalidOperationException("The rewritten filter is chunked; run it through LocalizedChunkedReads.") : _single;

    /// <summary>The disjoint chunked queries; empty when nothing can match.</summary>
    public IReadOnlyList<Expression<Func<T, bool>>> Chunks
        => _chunks ?? throw new InvalidOperationException("The rewritten filter is one query.");
}

/// <summary>
/// The one place that runs <see cref="LocalizedQueries{T}"/> against an entity store (TASK-686): every wrapper path and
/// <c>EntityTranslations</c> go through it, so no caller keeps its own chunk loop.
/// </summary>
internal static class LocalizedChunkedReads
{
    public static List<T> ReadAll<T>(IBulkStore<T> store, LocalizedQueries<T> queries) where T : AbstractModel
        => queries.IsChunked
            ? queries.Chunks.SelectMany(chunk => store.Read(chunk, null, null, null)).ToList()
            : store.Read(queries.Single, null, null, null).ToList();

    public static async Task<List<T>> ReadAllAsync<T>(IAsyncBulkStore<T> store, LocalizedQueries<T> queries, CancellationToken ct)
        where T : AbstractModel
    {
        if (!queries.IsChunked)
        {
            return (await store.ReadAsync(queries.Single, null, null, null, ct)).ToList();
        }
        var all = new List<T>();
        foreach (var chunk in queries.Chunks)
        {
            all.AddRange(await store.ReadAsync(chunk, null, null, null, ct));
        }
        return all;
    }

    /// <summary>The first match: the first chunk that has one.</summary>
    public static T? ReadFirst<T>(IBulkStore<T> store, LocalizedQueries<T> queries) where T : AbstractModel
    {
        if (!queries.IsChunked)
        {
            return ((IReadStore<T>)store).Read(queries.Single);
        }
        foreach (var chunk in queries.Chunks)
        {
            if (((IReadStore<T>)store).Read(chunk) is T found)
            {
                return found;
            }
        }
        return null;
    }

    /// <inheritdoc cref="ReadFirst"/>
    public static async Task<T?> ReadFirstAsync<T>(IAsyncBulkStore<T> store, LocalizedQueries<T> queries, CancellationToken ct)
        where T : AbstractModel
    {
        if (!queries.IsChunked)
        {
            return await ((IAsyncReadStore<T>)store).ReadAsync(queries.Single, ct);
        }
        foreach (var chunk in queries.Chunks)
        {
            if (await ((IAsyncReadStore<T>)store).ReadAsync(chunk, ct) is T found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>The match's size: summed over the chunks, which are disjoint.</summary>
    public static long Count<T>(IBulkStore<T> store, LocalizedQueries<T> queries) where T : AbstractModel
        => queries.IsChunked ? queries.Chunks.Sum(chunk => store.Count(chunk)) : store.Count(queries.Single);

    /// <inheritdoc cref="Count"/>
    public static async Task<long> CountAsync<T>(IAsyncBulkStore<T> store, LocalizedQueries<T> queries, CancellationToken ct)
        where T : AbstractModel
    {
        if (!queries.IsChunked)
        {
            return await store.CountAsync(queries.Single, ct);
        }
        long total = 0;
        foreach (var chunk in queries.Chunks)
        {
            total += await store.CountAsync(chunk, ct);
        }
        return total;
    }

    /// <summary>At most <paramref name="limit"/> matches, the limit counted across chunks, stopping once it is reached.</summary>
    public static async Task<List<T>> ReadUpToAsync<T>(IAsyncBulkStore<T> store, LocalizedQueries<T> queries, int limit, CancellationToken ct)
        where T : AbstractModel
    {
        if (!queries.IsChunked)
        {
            return (await store.ReadAsync(queries.Single, null, limit, null, ct)).ToList();
        }
        var found = new List<T>();
        foreach (var chunk in queries.Chunks)
        {
            found.AddRange(await store.ReadAsync(chunk, null, limit - found.Count, null, ct));
            if (found.Count >= limit)
            {
                break;
            }
        }
        return found;
    }

    /// <summary>
    /// A chunked list read with its order and page, decided by the user (TASK-686): every chunk is read, the whole match is
    /// localized, then sorted and paged in memory, as the localized-sort path already does. Only the page is returned.
    /// </summary>
    public static List<T> OrderAndPage<T>(List<T> localized, OrderBy<T>? orderBy, int? limit, int? offset, CultureInfo culture)
        where T : AbstractModel
    {
        var ordered = orderBy != null ? LocalizedOrderByHelper.ApplyInMemoryOrderBy(localized, orderBy, culture) : localized;
        return LocalizedOrderByHelper.ApplyInMemoryPaging(ordered, offset, limit);
    }
}
