using Birko.Data.Stores;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.MongoDB.Aggregation
{
    /// <summary>
    /// Shared helper for building MongoDB aggregation pipeline stages and mapping BSON results.
    /// Used by both <see cref="Stores.MongoDBStore{T}"/> and <see cref="Stores.AsyncMongoDBStore{T}"/>.
    /// </summary>
    public static class StoreAggregationHelper
    {
        /// <summary>
        /// Returns the MongoDB accumulator operator for the given aggregate function.
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        public static string GetMongoOperator(AggregateFunction function)
        {
            return function switch
            {
                AggregateFunction.Sum => "$sum",
                AggregateFunction.Avg => "$avg",
                AggregateFunction.Min => "$min",
                AggregateFunction.Max => "$max",
                AggregateFunction.Count => "$sum",
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

        /// <summary>
        /// Builds a BSON accumulator expression for the given aggregate function.
        /// For Count, returns { "$sum": 1 }. For others, returns { "$op": "$fieldPath" }.
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        public static BsonValue BuildAccumulatorExpression(AggregateFunction function, string? sourceFieldPath)
        {
            var op = GetMongoOperator(function);
            if (function == AggregateFunction.Count)
            {
                return new BsonDocument(op, 1);
            }
            return new BsonDocument(op, "$" + sourceFieldPath);
        }

        /// <summary>
        /// Builds the $group and $project stage documents for a MongoDB aggregation pipeline.
        /// </summary>
        /// <typeparam name="T">The model type.</typeparam>
        /// <param name="query">The aggregation query specification.</param>
        /// <returns>A tuple containing the $group document and $project document.</returns>
        public static (BsonDocument groupDoc, BsonDocument projection) BuildGroupStage<T>(AggregateQuery<T> query)
            where T : Data.Models.AbstractModel
        {
            var groupId = new BsonDocument();
            foreach (var field in query.GroupByFields)
            {
                groupId[field] = "$" + field;
            }

            var groupAccumulators = new BsonDocument();
            foreach (var agg in query.Aggregates)
            {
                groupAccumulators[agg.ResolvedAlias] = BuildAccumulatorExpression(agg.Function, agg.SourcePropertyName);
            }

            var groupDoc = new BsonDocument { { "_id", groupId } };
            foreach (var element in groupAccumulators.Elements)
            {
                groupDoc[element.Name] = element.Value;
            }

            var projection = new BsonDocument { { "_id", 0 } };
            foreach (var field in query.GroupByFields)
            {
                projection[field] = "$_id." + field;
            }
            foreach (var agg in query.Aggregates)
            {
                projection[agg.ResolvedAlias] = 1;
            }

            return (groupDoc, projection);
        }

        /// <summary>
        /// Builds the $group stage document from explicit field paths and accumulator definitions.
        /// Used by view translators that need custom field path resolution (e.g., joined fields).
        /// </summary>
        /// <param name="groupByFields">Field name → field path mappings for the _id document.</param>
        /// <param name="firstFields">Fields to carry forward via $first (field name → field path).</param>
        /// <param name="accumulators">Accumulator definitions (output name, function, source field path).</param>
        /// <returns>The $group stage BSON document.</returns>
        public static BsonDocument BuildGroupStageFromPaths(
            IEnumerable<(string Name, string FieldPath)> groupByFields,
            IEnumerable<(string Name, string FieldPath)>? firstFields,
            IEnumerable<(string OutputName, AggregateFunction Function, string? SourceFieldPath)> accumulators)
        {
            var groupId = new BsonDocument();
            foreach (var (name, fieldPath) in groupByFields)
            {
                groupId[name] = "$" + fieldPath;
            }

            var groupDoc = new BsonDocument("_id", groupId.ElementCount > 0 ? (BsonValue)groupId : BsonNull.Value);

            if (firstFields != null)
            {
                foreach (var (name, fieldPath) in firstFields)
                {
                    groupDoc.Add(name, new BsonDocument("$first", "$" + fieldPath));
                }
            }

            foreach (var (outputName, function, sourceFieldPath) in accumulators)
            {
                groupDoc.Add(outputName, BuildAccumulatorExpression(function, sourceFieldPath));
            }

            return groupDoc;
        }

        /// <summary>
        /// Maps a list of BSON result documents to <see cref="AggregateResult"/> instances
        /// using <see cref="BsonTypeMapper.MapToDotNetValue"/>.
        /// </summary>
        /// <param name="bsonResults">The BSON documents from the aggregation pipeline.</param>
        /// <returns>A read-only list of aggregate results.</returns>
        public static IReadOnlyList<AggregateResult> MapBsonResults(List<BsonDocument> bsonResults)
        {
            return bsonResults.Select(doc =>
            {
                var dict = new Dictionary<string, object?>();
                foreach (var element in doc.Elements)
                {
                    try
                    {
                        dict[element.Name] = BsonTypeMapper.MapToDotNetValue(element.Value);
                    }
                    catch
                    {
                        dict[element.Name] = element.Value?.ToString();
                    }
                }
                return new AggregateResult(dict);
            }).ToList().AsReadOnly();
        }
    }
}
