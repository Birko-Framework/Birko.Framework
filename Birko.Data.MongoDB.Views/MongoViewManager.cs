using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Views;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.Views;

/// <summary>
/// MongoDB implementation of <see cref="IViewManager"/>.
/// Creates/drops MongoDB views (virtual collections backed by aggregation pipelines).
/// Requires MongoDB 3.4+.
/// </summary>
public class MongoViewManager : IViewManager
{
    private readonly IMongoDatabase _database;

    public MongoViewManager(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public MongoViewManager(MongoDBClient client)
        : this(client?.Database ?? throw new ArgumentNullException(nameof(client)))
    {
    }

    public async Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        if (definition.QueryMode == Views.ViewQueryMode.OnTheFly)
        {
            return;
        }

        var viewName = definition.Name;
        if (string.IsNullOrEmpty(viewName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        // Check if view already exists
        if (await ExistsAsync(viewName, ct).ConfigureAwait(false))
        {
            return;
        }

        var sourceCollection = MongoViewTranslator.GetCollectionName(definition.PrimarySource);
        var pipeline = MongoViewTranslator.TranslatePipeline(definition);

        // MongoDB db.createView(viewName, source, pipeline)
        var command = new BsonDocument
        {
            { "create", viewName },
            { "viewOn", sourceCollection },
            { "pipeline", new BsonArray(pipeline) }
        };

        await _database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DropAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        await _database.DropCollectionAsync(viewName, ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var filter = new BsonDocument("name", viewName);
        var collections = await _database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = filter }, ct).ConfigureAwait(false);
        return await collections.AnyAsync(ct).ConfigureAwait(false);
    }

    public Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        // MongoDB views are virtual — they are always up-to-date.
        // No refresh needed.
        return Task.CompletedTask;
    }
}
