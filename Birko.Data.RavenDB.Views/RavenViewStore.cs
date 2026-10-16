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
using Raven.Client.Documents.Operations.Indexes;
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
        var query = await BuildQueryAsync(session, ct);

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

        // The pipeline (Where + OrderByHelper reflection + Skip/Take) preserves the Raven query
        // provider, so `sorted` is an IRavenQueryable at runtime and ToListAsync is available. Guard
        // the assumption with a clear message rather than a bare InvalidCastException, in case a future
        // helper ever materializes to an in-memory IQueryable.
        if (sorted is not IRavenQueryable<TView> ravenQuery)
        {
            throw new InvalidOperationException(
                "RavenViewStore query pipeline produced a non-Raven IQueryable; ToListAsync requires a RavenDB query provider.");
        }

        return await Project(ravenQuery).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        using var session = _documentStore.OpenAsyncSession();
        var query = await BuildQueryAsync(session, ct);

        if (filter != null)
        {
            query = query.Where(filter);
        }

        return await Project(query).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        using var session = _documentStore.OpenAsyncSession();
        var query = await BuildQueryAsync(session, ct);

        if (filter != null)
        {
            return await query.CountAsync(filter, ct);
        }

        return await query.CountAsync(ct);
    }

    /// <summary>
    /// A map-only index answers with source documents; the view is projected from its stored fields (SH-M399). A
    /// map-reduce index already answers with its reduce results.
    /// </summary>
    private IRavenQueryable<TView> Project(IRavenQueryable<TView> query)
        => _definition.HasAggregates ? query : query.ProjectInto<TView>();

    private async Task<IRavenQueryable<TView>> BuildQueryAsync(IAsyncDocumentSession session, CancellationToken ct)
    {
        // TASK-151 (SH-M395): OnTheFly queried session.Query<TView>(), the collection named after the VIEW type, so it
        // ignored the definition and answered empty. RavenDB has no ad-hoc join or map-reduce, so a view is an index.
        if (_definition.QueryMode == ViewQueryMode.OnTheFly)
        {
            throw new NotSupportedException(
                $"A RavenDB view is a static index: OnTheFly cannot evaluate view '{typeof(TView).Name}'. Use Persistent (or Auto) and create the index with IViewManager.EnsureAsync.");
        }

        if (_definition.QueryMode == ViewQueryMode.Auto)
        {
            // TASK-151 (SH-M396): the index was queried with no existence check. With no on-the-fly form to fall
            // back to (above), a missing index is reported here by name instead of as an index error mid-query.
            if (string.IsNullOrEmpty(_indexName)
                || await _documentStore.Maintenance.SendAsync(new GetIndexOperation(_indexName), ct).ConfigureAwait(false) == null)
            {
                throw new InvalidOperationException(
                    $"View index '{_indexName}' does not exist, and RavenDB has no on-the-fly view to fall back to. Create it with IViewManager.EnsureAsync.");
            }

            return session.Query<TView>(_indexName);
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
