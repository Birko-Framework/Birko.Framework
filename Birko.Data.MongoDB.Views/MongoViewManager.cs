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
        if (definition.QueryMode == Birko.Data.Views.ViewQueryMode.OnTheFly)
        {
            return;
        }

        var viewName = definition.Name;
        if (string.IsNullOrEmpty(viewName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        var sourceCollection = MongoViewTranslator.GetCollectionName(definition.PrimarySource);
        var pipeline = MongoViewTranslator.TranslatePipeline(definition);

        // TASK-151 (SH-M406): "creates or updates". An existing view was left as it was, so a changed definition kept
        // being read through the stale pipeline. collMod replaces a view's source and pipeline in place.
        if (await ExistsAsync(viewName, ct).ConfigureAwait(false))
        {
            await _database.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "collMod", viewName },
                { "viewOn", sourceCollection },
                { "pipeline", new BsonArray(pipeline) }
            }, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

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

        // TASK-151 (SH-M393): only a VIEW is dropped. Any collection of that name was, so DropAsync("Order") (the
        // name derived for an unnamed view is the source collection's) destroyed the data collection.
        var any = await (await _database.ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", viewName) }, ct).ConfigureAwait(false))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (any == null)
        {
            return;
        }
        if (any.GetValue("type", "collection").AsString != "view")
        {
            throw new InvalidOperationException(
                $"'{viewName}' is a {any.GetValue("type", "collection").AsString}, not a view; DropAsync drops only views.");
        }
        await _database.DropCollectionAsync(viewName, ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        // A VIEW of that name (TASK-151, SH-M393): an ordinary collection of the same name is not one.
        var filter = new BsonDocument { { "name", viewName }, { "type", "view" } };
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
