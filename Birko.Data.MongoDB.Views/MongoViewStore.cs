using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Views;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.Views;

/// <summary>
/// MongoDB implementation of <see cref="IViewStore{TView}"/>.
/// Executes aggregation pipelines translated from ViewDefinition.
/// </summary>
public class MongoViewStore<TView> : IViewStore<TView> where TView : class, new()
{
    private readonly IMongoDatabase _database;
    private readonly ViewDefinition _definition;
    private readonly List<BsonDocument> _basePipeline;

    public MongoViewStore(IMongoDatabase database, ViewDefinition definition)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _basePipeline = MongoViewTranslator.TranslatePipeline(definition);
    }

    public MongoViewStore(MongoDBClient client, ViewDefinition definition)
        : this(client?.Database ?? throw new ArgumentNullException(nameof(client)), definition)
    {
    }

    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        var pipeline = BuildPipeline(filter, orderBy, offset, limit);
        var results = await ExecutePipelineAsync(pipeline, ct).ConfigureAwait(false);
        return results.Select(DeserializeView);
    }

    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        var pipeline = BuildPipeline(filter, null, null, 1);
        var results = await ExecutePipelineAsync(pipeline, ct).ConfigureAwait(false);
        var first = results.FirstOrDefault();
        return first != null ? DeserializeView(first) : null;
    }

    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        var pipeline = BuildPipeline(filter, null, null, null);
        pipeline.Add(new BsonDocument("$count", "count"));

        var results = await ExecutePipelineAsync(pipeline, ct).ConfigureAwait(false);
        var countDoc = results.FirstOrDefault();
        return countDoc?.GetValue("count", 0).ToInt64() ?? 0;
    }

    private List<BsonDocument> BuildPipeline(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? offset,
        int? limit)
    {
        var pipeline = new List<BsonDocument>(_basePipeline);

        // Add $match for filter (after $project, so it works on view fields)
        if (filter != null)
        {
            var filterDef = Builders<TView>.Filter.Where(filter);
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<TView>();
            var renderArgs = new RenderArgs<TView>(serializer, BsonSerializer.SerializerRegistry);
            var rendered = filterDef.Render(renderArgs);
            pipeline.Add(new BsonDocument("$match", rendered));
        }

        // Add $sort
        if (orderBy?.Fields != null && orderBy.Fields.Any())
        {
            var sortDoc = new BsonDocument();
            foreach (var field in orderBy.Fields)
            {
                sortDoc.Add(field.PropertyName, field.Descending ? -1 : 1);
            }
            pipeline.Add(new BsonDocument("$sort", sortDoc));
        }

        // Add $skip and $limit
        if (offset.HasValue && offset.Value > 0)
        {
            pipeline.Add(new BsonDocument("$skip", offset.Value));
        }

        if (limit.HasValue && limit.Value > 0)
        {
            pipeline.Add(new BsonDocument("$limit", limit.Value));
        }

        return pipeline;
    }

    private async Task<List<BsonDocument>> ExecutePipelineAsync(List<BsonDocument> stages, CancellationToken ct)
    {
        var collectionName = MongoViewTranslator.GetCollectionName(_definition.PrimarySource);

        if (_definition.QueryMode == Birko.Data.Views.ViewQueryMode.Persistent ||
            _definition.QueryMode == Birko.Data.Views.ViewQueryMode.Auto)
        {
            // Try querying the persistent view (MongoDB view is a virtual collection)
            var viewName = _definition.Name;
            if (!string.IsNullOrEmpty(viewName))
            {
                try
                {
                    var viewCollection = _database.GetCollection<BsonDocument>(viewName);
                    var viewPipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
                    var cursor = await viewCollection.AggregateAsync(viewPipeline, null, ct).ConfigureAwait(false);
                    return await cursor.ToListAsync(ct).ConfigureAwait(false);
                }
                catch (MongoCommandException) when (_definition.QueryMode == Birko.Data.Views.ViewQueryMode.Auto)
                {
                    // Fall through to on-the-fly
                }
            }
        }

        // On-the-fly: run against the primary source collection
        var collection = _database.GetCollection<BsonDocument>(collectionName);
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
        var result = await collection.AggregateAsync(pipeline, null, ct).ConfigureAwait(false);
        return await result.ToListAsync(ct).ConfigureAwait(false);
    }

    private static TView DeserializeView(BsonDocument doc)
    {
        return BsonSerializer.Deserialize<TView>(doc);
    }
}
