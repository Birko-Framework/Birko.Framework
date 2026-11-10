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
        /// <exception cref="ArgumentException">The query has nothing to aggregate (TASK-886; it answered one empty row).</exception>
        public static (BsonDocument groupDoc, BsonDocument projection) BuildGroupStage<T>(AggregateQuery<T> query)
            where T : Data.Models.AbstractModel
        {
            query.ThrowIfNothingToAggregate();
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

        /// <summary>
        /// TASK-886: the store aggregate's answer from the pipeline's documents, with the edges the in-memory aggregation
        /// answers. The one producer behind <see cref="Stores.MongoDBStore{T}.Aggregate(AggregateQuery{T})"/> and its async twin.
        /// </summary>
        /// <remarks>
        /// <para>An ungrouped query is grouped on a constant, and MongoDB's <c>$group</c> answers no document over no input:
        /// it answered no rows where every provider now answers one (the user's decision, 2026-11-10). Over no input the answer
        /// is <see cref="AggregateHelper.LinqAggregate{T}"/>'s over no rows: Count 0, Sum 0, the others null.</para>
        /// <para>Each Count, Sum and Avg, and a numeric Min / Max, is read into the type
        /// <see cref="AggregateMath.ResultType"/> names. Measured before: a <c>$sum</c> over only nulls answered an
        /// <c>Int32</c> 0 for a <c>decimal?</c> member, and a decimal Sum / Avg a <c>Decimal128</c>, not a <c>decimal</c>. A
        /// non-numeric Min / Max is left as the driver maps it.</para>
        /// </remarks>
        public static IReadOnlyList<AggregateResult> ReadResults<T>(AggregateQuery<T> query, List<BsonDocument> bsonResults)
            where T : Data.Models.AbstractModel
        {
            if (bsonResults.Count == 0 && !query.IsGrouped)
            {
                return AggregateHelper.LinqAggregate(Array.Empty<T>(), query);
            }

            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var aggregate in query.Aggregates)
            {
                var type = ResultTypeOf<T>(aggregate);
                if (type != null) types[aggregate.ResolvedAlias] = type;
            }

            var rows = new List<AggregateResult>(bsonResults.Count);
            foreach (var mapped in MapBsonResults(bsonResults))
            {
                var row = new Dictionary<string, object?>(mapped.Values);
                foreach (var (alias, type) in types)
                {
                    if (row.TryGetValue(alias, out var value) && value != null) row[alias] = ToNumber(value, type);
                }
                rows.Add(new AggregateResult(row));
            }
            return rows.AsReadOnly();
        }

        /// <summary>The numeric type an aggregate answers in, or null where it is not numeric or names no member.</summary>
        private static Type? ResultTypeOf<T>(AggregateField aggregate)
        {
            if (aggregate.Function == AggregateFunction.Count) return typeof(int);
            var member = typeof(T).GetProperty(aggregate.SourcePropertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (member == null) return null;
            var underlying = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (!IsNumber(underlying)) return null;
            return AggregateMath.ResultType(aggregate.Function, member.PropertyType);
        }

        private static bool IsNumber(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double) || type == typeof(float)
               || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint)
               || type == typeof(ulong);

        /// <summary>A number the driver mapped (an <c>int</c>, <c>long</c>, <c>double</c> or <c>Decimal128</c>) as <paramref name="type"/>, checked.</summary>
        private static object ToNumber(object value, Type type)
        {
            if (value is Decimal128 d128) value = Decimal128.ToDecimal(d128);
            return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
