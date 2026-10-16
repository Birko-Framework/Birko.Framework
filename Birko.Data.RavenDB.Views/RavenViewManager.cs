using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Views;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;

namespace Birko.Data.RavenDB.Views;

/// <summary>
/// RavenDB implementation of <see cref="IViewManager"/>.
/// Manages static indexes that back persistent views.
/// </summary>
public class RavenViewManager : IViewManager
{
    private readonly IDocumentStore _documentStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="RavenViewManager"/> class.
    /// </summary>
    /// <param name="documentStore">The RavenDB document store.</param>
    public RavenViewManager(IDocumentStore documentStore)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    }

    /// <inheritdoc />
    public async Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.QueryMode == ViewQueryMode.OnTheFly)
        {
            return;
        }

        var indexName = definition.Name;
        if (string.IsNullOrEmpty(indexName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        var (map, reduce) = RavenViewTranslator.TranslateToMapReduce(definition, _documentStore.Conventions.GetCollectionName);

        var indexDefinition = new IndexDefinition
        {
            Name = indexName,
            Maps = { map }
        };

        if (reduce != null)
        {
            indexDefinition.Reduce = reduce;
        }
        else
        {
            // TASK-151 (SH-M399): a map-only index answers with the SOURCE documents unless its fields are stored, so
            // a renamed or joined field never reached the view. Stored, the store projects the view from the index.
            foreach (var field in definition.Fields)
            {
                indexDefinition.Fields[field.ViewProperty] = new IndexFieldOptions { Storage = FieldStorage.Yes };
            }
        }

        await _documentStore.Maintenance.SendAsync(new PutIndexesOperation(indexDefinition), ct);
    }

    /// <inheritdoc />
    public async Task DropAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        await _documentStore.Maintenance.SendAsync(new DeleteIndexOperation(viewName), ct);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var index = await _documentStore.Maintenance.SendAsync(new GetIndexOperation(viewName), ct);
        return index != null;
    }

    /// <inheritdoc />
    public Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        // RavenDB indexes are auto-updated — no manual refresh needed.
        return Task.CompletedTask;
    }
}
