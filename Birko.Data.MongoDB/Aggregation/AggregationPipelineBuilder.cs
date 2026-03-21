using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.Aggregation
{
    /// <summary>
    /// Fluent builder for constructing and executing MongoDB aggregation pipelines.
    /// </summary>
    /// <typeparam name="T">The document type of the source collection.</typeparam>
    public class AggregationPipelineBuilder<T> where T : class
    {
        private readonly IMongoCollection<T> _collection;
        private readonly List<BsonDocument> _stages;

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregationPipelineBuilder{T}"/> class.
        /// </summary>
        /// <param name="collection">The MongoDB collection to aggregate over.</param>
        public AggregationPipelineBuilder(IMongoCollection<T> collection)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _stages = new List<BsonDocument>();
        }

        private RenderArgs<T> CreateRenderArgs()
        {
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            return new RenderArgs<T>(serializer, BsonSerializer.SerializerRegistry);
        }

        /// <summary>
        /// Adds a $match stage using a filter definition.
        /// </summary>
        /// <param name="filter">The filter definition to match documents against.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Match(FilterDefinition<T> filter)
        {
            if (filter == null)
            {
                return this;
            }

            var renderedFilter = filter.Render(CreateRenderArgs());
            _stages.Add(new BsonDocument("$match", renderedFilter));
            return this;
        }

        /// <summary>
        /// Adds a $match stage using a predicate expression.
        /// </summary>
        /// <param name="predicate">The predicate expression to match documents against.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Match(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
            {
                return this;
            }

            var filter = Builders<T>.Filter.Where(predicate);
            return Match(filter);
        }

        /// <summary>
        /// Adds a $group stage.
        /// </summary>
        /// <param name="groupExpression">The BSON document defining the group expression.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Group(BsonDocument groupExpression)
        {
            if (groupExpression == null)
            {
                return this;
            }

            _stages.Add(new BsonDocument("$group", groupExpression));
            return this;
        }

        /// <summary>
        /// Adds a $sort stage.
        /// </summary>
        /// <param name="sort">The sort definition.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Sort(SortDefinition<T> sort)
        {
            if (sort == null)
            {
                return this;
            }

            var renderedSort = sort.Render(CreateRenderArgs());
            _stages.Add(new BsonDocument("$sort", renderedSort));
            return this;
        }

        /// <summary>
        /// Adds a $project stage using a projection definition.
        /// </summary>
        /// <param name="projection">The projection definition.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Project(ProjectionDefinition<T, BsonDocument> projection)
        {
            if (projection == null)
            {
                return this;
            }

            var renderedProjection = projection.Render(CreateRenderArgs());
            _stages.Add(new BsonDocument("$project", renderedProjection.Document));
            return this;
        }

        /// <summary>
        /// Adds a $project stage using a BSON document.
        /// </summary>
        /// <param name="projection">The BSON document defining the projection.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Project(BsonDocument projection)
        {
            if (projection == null)
            {
                return this;
            }

            _stages.Add(new BsonDocument("$project", projection));
            return this;
        }

        /// <summary>
        /// Adds a $limit stage.
        /// </summary>
        /// <param name="count">The maximum number of documents to pass through.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Limit(int count)
        {
            _stages.Add(new BsonDocument("$limit", count));
            return this;
        }

        /// <summary>
        /// Adds a $skip stage.
        /// </summary>
        /// <param name="count">The number of documents to skip.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Skip(int count)
        {
            _stages.Add(new BsonDocument("$skip", count));
            return this;
        }

        /// <summary>
        /// Adds an $unwind stage for a field specified by name.
        /// </summary>
        /// <param name="fieldName">The name of the array field to unwind (with or without '$' prefix).</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Unwind(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return this;
            }

            var path = fieldName.StartsWith("$") ? fieldName : "$" + fieldName;
            _stages.Add(new BsonDocument("$unwind", path));
            return this;
        }

        /// <summary>
        /// Adds an $unwind stage for a field specified by a field definition.
        /// </summary>
        /// <param name="field">The field definition of the array field to unwind.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Unwind(FieldDefinition<T> field)
        {
            if (field == null)
            {
                return this;
            }

            var renderedField = field.Render(CreateRenderArgs());
            var path = "$" + renderedField.FieldName;
            _stages.Add(new BsonDocument("$unwind", path));
            return this;
        }

        /// <summary>
        /// Adds a $lookup stage for performing a left outer join with another collection.
        /// </summary>
        /// <param name="from">The foreign collection name.</param>
        /// <param name="localField">The local field to join on.</param>
        /// <param name="foreignField">The foreign field to join on.</param>
        /// <param name="as">The output array field name.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Lookup(string from, string localField, string foreignField, string @as)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(localField) || string.IsNullOrEmpty(foreignField) || string.IsNullOrEmpty(@as))
            {
                return this;
            }

            _stages.Add(new BsonDocument("$lookup", new BsonDocument
            {
                { "from", from },
                { "localField", localField },
                { "foreignField", foreignField },
                { "as", @as }
            }));
            return this;
        }

        /// <summary>
        /// Adds a $count stage that counts the number of documents.
        /// </summary>
        /// <param name="fieldName">The name of the output field containing the count.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> Count(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return this;
            }

            _stages.Add(new BsonDocument("$count", fieldName));
            return this;
        }

        /// <summary>
        /// Adds an $addFields stage.
        /// </summary>
        /// <param name="fields">The BSON document defining the fields to add.</param>
        /// <returns>This builder instance for chaining.</returns>
        public AggregationPipelineBuilder<T> AddFields(BsonDocument fields)
        {
            if (fields == null)
            {
                return this;
            }

            _stages.Add(new BsonDocument("$addFields", fields));
            return this;
        }

        /// <summary>
        /// Executes the aggregation pipeline asynchronously and returns all results.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A list of BSON documents representing the pipeline results.</returns>
        public async Task<List<BsonDocument>> ToListAsync(CancellationToken ct = default)
        {
            var pipeline = PipelineDefinition<T, BsonDocument>.Create(_stages);
            var cursor = await _collection.AggregateAsync(pipeline, null, ct).ConfigureAwait(false);
            return await cursor.ToListAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the aggregation pipeline asynchronously and returns the first result or null.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The first BSON document result, or null if the pipeline produces no results.</returns>
        public async Task<BsonDocument?> FirstOrDefaultAsync(CancellationToken ct = default)
        {
            var pipeline = PipelineDefinition<T, BsonDocument>.Create(_stages);
            var cursor = await _collection.AggregateAsync(pipeline, null, ct).ConfigureAwait(false);
            return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the aggregation pipeline synchronously and returns all results.
        /// </summary>
        /// <returns>A list of BSON documents representing the pipeline results.</returns>
        public List<BsonDocument> ToList()
        {
            var pipeline = PipelineDefinition<T, BsonDocument>.Create(_stages);
            var cursor = _collection.Aggregate(pipeline);
            return cursor.ToList();
        }

        /// <summary>
        /// Executes the aggregation pipeline synchronously and returns the first result or null.
        /// </summary>
        /// <returns>The first BSON document result, or null if the pipeline produces no results.</returns>
        public BsonDocument? FirstOrDefault()
        {
            var pipeline = PipelineDefinition<T, BsonDocument>.Create(_stages);
            var cursor = _collection.Aggregate(pipeline);
            return cursor.FirstOrDefault();
        }
    }
}
