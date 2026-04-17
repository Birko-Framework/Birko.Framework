using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Views;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace Birko.Data.RavenDB.Views;

/// <summary>
/// RavenDB implementation of <see cref="IViewStore{TView}"/>.
/// For Persistent/Auto modes, queries a static index by name.
/// For OnTheFly mode, queries the primary collection directly.
/// </summary>
public class RavenViewStore<TView> : IViewStore<TView> where TView : class
{
    private readonly IDocumentStore _documentStore;
    private readonly ViewDefinition _definition;
    private readonly string? _indexName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RavenViewStore{TView}"/> class.
    /// </summary>
    /// <param name="documentStore">The RavenDB document store.</param>
    /// <param name="definition">The view definition.</param>
    public RavenViewStore(IDocumentStore documentStore, ViewDefinition definition)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _indexName = definition.Name;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        using var session = _documentStore.OpenAsyncSession();
        var query = BuildQuery(session);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        IQueryable<TView> sorted = query;
        sorted = OrderByHelper.ApplyTo(sorted, orderBy);

        if (offset.HasValue)
        {
            sorted = sorted.Skip(offset.Value);
        }

        if (limit.HasValue)
        {
            sorted = sorted.Take(limit.Value);
        }

        return await ((IRavenQueryable<TView>)sorted).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        using var session = _documentStore.OpenAsyncSession();
        var query = BuildQuery(session);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        using var session = _documentStore.OpenAsyncSession();
        var query = BuildQuery(session);

        if (filter != null)
        {
            return await query.CountAsync(filter, ct);
        }

        return await query.CountAsync(ct);
    }

    private IRavenQueryable<TView> BuildQuery(IAsyncDocumentSession session)
    {
        if (_definition.QueryMode == ViewQueryMode.OnTheFly)
        {
            return session.Query<TView>();
        }

        if (_definition.QueryMode == ViewQueryMode.Auto)
        {
            // Try persistent index first; fall back to dynamic query
            if (!string.IsNullOrEmpty(_indexName))
            {
                return session.Query<TView>(_indexName);
            }

            return session.Query<TView>();
        }

        // Persistent mode — query the static index
        if (string.IsNullOrEmpty(_indexName))
        {
            throw new InvalidOperationException(
                "View name (index name) is required for Persistent query mode.");
        }

        return session.Query<TView>(_indexName);
    }

}
