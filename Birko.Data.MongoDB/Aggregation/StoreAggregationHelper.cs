using Birko.Data.Stores;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
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
        /// <exception cref="ArgumentException">The query has nothing to aggregate (TASK-886; it answered one empty row), or
        /// names a group-by field, aggregate source or time column that is no property of <typeparamref name="T"/>, or its
        /// time bucket is incomplete or not a positive interval (as <see cref="AggregateHelper.LinqAggregate{T}"/> refuses them).</exception>
        /// <exception cref="NotSupportedException">A named member is not stored, or the time bucket cannot be answered as
        /// the in-memory aggregation answers it (see <see cref="TimeBucketExpression{T}"/>).</exception>
        /// <remarks>
        /// TASK-906: each name is resolved as the in-memory aggregation resolves it (a property, case-insensitively) and read
        /// at the element the driver stores it under (<c>_id</c> for the identity). The raw name used to be read, so a
        /// differently-cased name or the identity grouped every row as null, and a time bucket was ignored (measured: one row
        /// per kind where C# has one per hour).
        /// </remarks>
        public static (BsonDocument groupDoc, BsonDocument projection) BuildGroupStage<T>(AggregateQuery<T> query)
            where T : Data.Models.AbstractModel
        {
            query.ThrowIfNothingToAggregate();
            query.ThrowIfStringOrderNotOrdinal(Owner<T>());
            var groupId = new BsonDocument();
            var projection = new BsonDocument { { "_id", 0 } };
            for (var i = 0; i < query.GroupByFields.Count; i++)
            {
                var field = query.GroupByFields[i];
                groupId["k" + i] = "$" + Member<T>(field, "group-by field").ElementName;
                projection[field] = "$_id.k" + i;
            }

            var bucket = TimeBucketExpression<T>(query);
            if (bucket != null)
            {
                groupId["bucket"] = bucket;
                projection[AggregateBucketAlias] = "$_id.bucket";
            }

            var groupDoc = new BsonDocument { { "_id", groupId } };
            for (var i = 0; i < query.Aggregates.Count; i++)
            {
                var agg = query.Aggregates[i];
                projection[agg.ResolvedAlias] = 1;
                if (agg.Function == AggregateFunction.Count)
                {
                    groupDoc[agg.ResolvedAlias] = BuildAccumulatorExpression(agg.Function, null);
                    continue;
                }
                var member = Member<T>(agg.SourcePropertyName, "aggregate source");
                if (agg.Function is not (AggregateFunction.Min or AggregateFunction.Max))
                {
                    groupDoc[agg.ResolvedAlias] = BuildAccumulatorExpression(agg.Function, member.ElementName);
                    continue;
                }
                var (extreme, checks) = ExtremeAccumulators(agg.Function, member, Owner<T>(), $"{typeof(T).Name}.{agg.SourcePropertyName}");
                groupDoc[agg.ResolvedAlias] = extreme;
                foreach (var (suffix, accumulator) in checks)
                {
                    groupDoc[CheckName(CheckOwner(i), suffix)] = accumulator;
                    projection[CheckName(CheckOwner(i), suffix)] = 1;
                }
            }

            return (groupDoc, projection);
        }

        /// <summary>The result key of the time bucket, as in memory.</summary>
        private const string AggregateBucketAlias = "bucket_time";

        private static string Owner<T>() => $"MongoDB aggregate of {typeof(T).Name}";

        private static string CheckOwner(int aggregateIndex) => "a" + aggregateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// TASK-907: the <c>$min</c> / <c>$max</c> of a member, in the order <see cref="AggregateHelper"/> takes it, and the
        /// hidden accumulators that check the group answers it exactly (named by <c>suffix</c>, read by
        /// <see cref="ThrowIfUnsound"/>).
        /// </summary>
        /// <remarks>
        /// <para>A string is compared by MongoDB byte by byte (its simple collation), code-point order, which is Ordinal
        /// (measured: <c>"Banana"</c> below <c>"apple"</c>, <c>"a"</c> below <c>"a "</c>, <c>"ss"</c> below <c>"ß"</c>) but for
        /// a group holding both a character above U+FFFF and one in U+E000–U+FFFF, where UTF-16 puts the surrogate pair first.
        /// Two flags (<c>$regexMatch</c>) find such a group, which is refused by name; every other group is answered.
        /// Another comparison than Ordinal is refused before any request (<see cref="AggregateQuery{T}.ThrowIfStringOrderNotOrdinal"/>).</para>
        /// <para>An enum stored as its name (<c>[BsonRepresentation(BsonType.String)]</c>) was compared by its name (measured:
        /// the Min of <c>Zero</c> and <c>High</c> was <c>High</c>); C# compares an enum by its value. The extreme is taken over
        /// the value: a name is looked up in the enum's names, a number (a document written before the representation
        /// changed, which the driver reads as the value) is taken as it is; a number was skipped (measured: the Max of 30 and
        /// <c>"Low"</c> was <c>Low</c>). A name the enum does not have fails the read, as reading its document does. A [Flags]
        /// enum stored as text is refused: a combination is stored as <c>"Read, Write"</c>, which no list of names orders.</para>
        /// </remarks>
        private static (BsonValue Extreme, List<(string Suffix, BsonValue Accumulator)> Checks) ExtremeAccumulators(
            AggregateFunction function, (string ElementName, IBsonSerializer Serializer, Type Type) member, string owner, string memberName)
        {
            var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
            var field = "$" + member.ElementName;
            if (type == typeof(string)) return (BuildAccumulatorExpression(function, member.ElementName), StringOrderChecks(field));

            var names = TextEnumNames(member.Serializer, member.Type, owner, function, memberName);
            if (names == null) return (BuildAccumulatorExpression(function, member.ElementName), new());

            var isText = new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", field), "string" });
            var position = new BsonDocument("$indexOfArray", new BsonArray { new BsonArray(names), field });
            var unknown = new BsonDocument("$eq", new BsonArray { position, -1 });
            var values = new BsonArray(names.Select(n => (BsonValue)new BsonInt64(Convert.ToInt64(Enum.Parse(type, n), System.Globalization.CultureInfo.InvariantCulture))));
            var key = new BsonDocument("$cond", new BsonArray
            {
                isText,
                new BsonDocument("$cond", new BsonArray { unknown, BsonNull.Value, new BsonDocument("$arrayElemAt", new BsonArray { values, position }) }),
                new BsonDocument("$cond", new BsonArray { new BsonDocument("$isNumber", field), field, BsonNull.Value }),
            });
            var unknownFlag = new BsonDocument("$max", new BsonDocument("$cond", new BsonArray
            {
                isText, new BsonDocument("$cond", new BsonArray { unknown, 1, 0 }), 0,
            }));
            return (new BsonDocument(GetMongoOperator(function), key), new() { (UnknownName, unknownFlag) });
        }

        private const string HiddenPrefix = "__birko907_";
        private const string HighBmp = "bmp";
        private const string Astral = "astral";
        private const string UnknownName = "unknown";

        /// <summary>TASK-907: the name of a hidden check accumulator of the aggregate (or view property) <paramref name="owner"/>.</summary>
        public static string CheckName(string owner, string suffix) => HiddenPrefix + owner + "_" + suffix;

        /// <summary>
        /// TASK-907: the two flags of a string Min / Max: whether the group holds a character in U+E000–U+FFFF, and one above
        /// U+FFFF. The one producer for the store and the view.
        /// </summary>
        public static List<(string Suffix, BsonValue Accumulator)> StringOrderChecks(string field)
        {
            BsonValue Flag(string pattern) => new BsonDocument("$max", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", field), "string" }),
                new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$regexMatch", new BsonDocument { { "input", field }, { "regex", pattern } }), 1, 0,
                }),
                0,
            }));
            return new() { (HighBmp, Flag(AggregateMath.HighBmpCharacters)), (Astral, Flag(AggregateMath.AstralCharacters)) };
        }

        /// <summary>
        /// TASK-907: refuses a result document whose checks say its Min / Max of <paramref name="owner"/> is not the one C#
        /// takes (a mixed group: <see cref="AggregateMath.MixedAstralNotSupported"/>; a name the enum does not have), and
        /// removes the checks from it.
        /// </summary>
        public static void ThrowIfUnsound(BsonDocument doc, string owner, AggregateFunction function, string provider, string memberName)
        {
            bool Set(string suffix) => doc.TryGetValue(CheckName(owner, suffix), out var v) && v.IsNumeric && v.ToInt64() > 0;
            var mixed = Set(HighBmp) && Set(Astral);
            var unknown = Set(UnknownName);
            foreach (var suffix in new[] { HighBmp, Astral, UnknownName }) doc.Remove(CheckName(owner, suffix));
            if (mixed) throw AggregateMath.MixedAstralNotSupported(provider, function, memberName);
            if (unknown)
            {
                throw new InvalidOperationException(
                    $"A stored value of {memberName} is not a name of its enum, so its {function} cannot be taken by value; a read "
                    + "of its document fails the same way. (TASK-907)");
            }
        }

        /// <summary>
        /// TASK-907: the names of the enum <paramref name="serializer"/> stores as text; null for any other member.
        /// </summary>
        /// <exception cref="NotSupportedException">The enum is a [Flags] enum, or a <c>ulong</c> one, stored as text.</exception>
        private static string[]? TextEnumNames(IBsonSerializer serializer, Type type, string owner, AggregateFunction function, string memberName)
        {
            var enumType = Nullable.GetUnderlyingType(type) ?? type;
            if (!enumType.IsEnum) return null;
            if (Nullable.GetUnderlyingType(type) != null && serializer is IChildSerializerConfigurable nullable) serializer = nullable.ChildSerializer;
            if (serializer is not IRepresentationConfigurable { Representation: BsonType.String }) return null;
            if (enumType.IsDefined(typeof(FlagsAttribute), false) || Enum.GetUnderlyingType(enumType) == typeof(ulong))
            {
                throw new NotSupportedException(
                    $"{owner} refused: {function} of {memberName}, a [Flags] or ulong enum {enumType.Name} stored as text. A "
                    + "combination is stored as its names joined (\"Read, Write\"), which MongoDB cannot order by value as C# does. "
                    + "Store it as a number, or read the rows and aggregate them in memory. (TASK-907)");
            }
            return Enum.GetNames(enumType);
        }

        /// <summary>TASK-907: an enum Min / Max as the extreme answered it: the value, a number.</summary>
        private static object? EnumOfValue(BsonValue value, Type type)
        {
            if (value.IsBsonNull || value.IsBsonUndefined) return null;
            return Enum.ToObject(Nullable.GetUnderlyingType(type) ?? type, value.ToInt64());
        }

        /// <summary>
        /// TASK-907: refuses a view's Min / Max that MongoDB would take in another order than C#: one of an enum stored as
        /// text, which a view's <c>$min</c> compares by name. A view's string extreme is Ordinal, as the store's is by default.
        /// </summary>
        /// <exception cref="NotSupportedException">The member is an enum stored as text.</exception>
        public static void ThrowIfViewExtremeUnordered(Type sourceType, string? property, AggregateFunction function, string owner)
        {
            if (function is not (AggregateFunction.Min or AggregateFunction.Max) || property == null) return;
            var member = sourceType.GetProperty(property, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (member == null) return;
            var memberMap = BsonClassMap.LookupClassMap(sourceType).AllMemberMaps.FirstOrDefault(m => m.MemberName == member.Name);
            if (memberMap == null) return;
            var memberName = $"{sourceType.Name}.{member.Name}";
            if (TextEnumNames(memberMap.GetSerializer(), member.PropertyType, owner, function, memberName) == null) return;
            throw new NotSupportedException(
                $"{owner} refused: {function} of {memberName}, an enum stored as text, which a MongoDB view would order by name; "
                + "C# orders an enum by its value. Store it as a number, or use the store's Aggregate. (TASK-907)");
        }

        /// <summary>
        /// TASK-906: the property <paramref name="name"/> names, looked up as <see cref="AggregateHelper"/> looks it up, and
        /// how the driver stores it.
        /// </summary>
        private static (string ElementName, IBsonSerializer Serializer, Type Type) Member<T>(string name, string role)
        {
            var property = typeof(T).GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)
                ?? throw new ArgumentException($"Aggregate {role} '{name}' is not a property of {typeof(T).Name}.", nameof(name));
            var classMap = global::MongoDB.Bson.Serialization.BsonClassMap.LookupClassMap(typeof(T));
            var memberMap = classMap.AllMemberMaps.FirstOrDefault(m => m.MemberName == property.Name)
                ?? throw new NotSupportedException(
                    $"MongoDB aggregate of {typeof(T).Name} refused: {role} '{property.Name}' is not stored in the document. (TASK-906)");
            var element = ReferenceEquals(memberMap, classMap.IdMemberMap) ? "_id" : memberMap.ElementName;
            return (element, memberMap.GetSerializer(), property.PropertyType);
        }

        /// <summary>
        /// TASK-906: the <c>$group</c> key of the query's time bucket, or <c>null</c> when it has none. The bucket is the
        /// stored time floored to the interval: <c>t - ((t mod ms) + ms) mod ms</c> in milliseconds since 1970, as a date; a
        /// row whose time is not a date (null, missing) is the null bucket, as in memory.
        /// </summary>
        /// <remarks>
        /// Refused where MongoDB cannot answer as <see cref="AggregateHelper.LinqAggregate{T}"/> does over the rows a read
        /// returns: a time column that is not a <see cref="DateTime"/>; a member not stored as the driver's default (a BSON
        /// date read back as UTC), since the in-memory bucket floors the time a read returns; an interval that is not whole
        /// milliseconds (a BSON date has no finer part); and an interval that does not divide the time from 0001-01-01 to
        /// 1970-01-01, since the in-memory bucket counts from 0001-01-01 (the rule TASK-878 and TASK-885 measured for
        /// Elasticsearch and Cosmos DB).
        /// </remarks>
        private static BsonValue? TimeBucketExpression<T>(AggregateQuery<T> query)
            where T : Data.Models.AbstractModel
        {
            if (string.IsNullOrEmpty(query.TimeBucketInterval) && string.IsNullOrEmpty(query.TimeColumn)) return null;
            if (string.IsNullOrEmpty(query.TimeBucketInterval) || string.IsNullOrEmpty(query.TimeColumn))
                throw new ArgumentException("Time bucketing needs both TimeBucketInterval and TimeColumn.", nameof(query));
            var span = TimeIntervalParser.Parse(query.TimeBucketInterval);
            if (span <= TimeSpan.Zero)
                throw new ArgumentException($"Time bucket interval '{query.TimeBucketInterval}' is not a positive interval.", nameof(query));

            var (element, serializer, type) = Member<T>(query.TimeColumn, "time column");
            var refused = $"MongoDB aggregate of {typeof(T).Name} refused: ";
            if ((Nullable.GetUnderlyingType(type) ?? type) != typeof(DateTime))
                throw new NotSupportedException(refused + $"time column '{query.TimeColumn}' is {type.Name}; only a DateTime member is bucketed. (TASK-906)");
            if (serializer is IChildSerializerConfigurable nullable) serializer = nullable.ChildSerializer;
            if (serializer is not DateTimeSerializer { Kind: DateTimeKind.Utc, Representation: BsonType.DateTime, DateOnly: false })
                throw new NotSupportedException(refused + $"time column '{query.TimeColumn}' is not stored as a BSON date read back "
                    + "as UTC (the driver's default), so its buckets would not be the ones the rows a read returns fall into. (TASK-906)");
            if (span.Ticks % TimeSpan.TicksPerMillisecond != 0)
                throw new NotSupportedException(refused + $"time bucket interval '{query.TimeBucketInterval}' is not a whole number "
                    + "of milliseconds; a stored date has no finer part. (TASK-906)");
            if (DateTime.UnixEpoch.Ticks % span.Ticks != 0)
                throw new NotSupportedException(refused + $"time bucket interval '{query.TimeBucketInterval}' does not divide the "
                    + "time from 0001-01-01 to 1970-01-01, so MongoDB's buckets would start at other times than the in-memory "
                    + "ones. Use an interval that divides a day. (TASK-906)");

            var ms = new BsonInt64(span.Ticks / TimeSpan.TicksPerMillisecond);
            var time = new BsonDocument("$toLong", "$" + element);
            var offset = new BsonDocument("$mod", new BsonArray
            {
                new BsonDocument("$add", new BsonArray { new BsonDocument("$mod", new BsonArray { time, ms }), ms }),
                ms,
            });
            return new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", "$" + element), "date" }),
                new BsonDocument("$toDate", new BsonDocument("$subtract", new BsonArray { time, offset })),
                BsonNull.Value,
            });
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
        /// <c>Int32</c> 0 for a <c>decimal?</c> member, and a decimal Sum / Avg a <c>Decimal128</c>, not a <c>decimal</c>.</para>
        /// <para>TASK-906: a group key, a time bucket and a Min / Max are read with their member's serializer, so each is the
        /// value a read of that member returns (measured before: an enum key or Max answered its stored <c>Int32</c>). The
        /// query's <see cref="AggregateQuery{T}.OrderBy"/>, <see cref="AggregateQuery{T}.Offset"/> and
        /// <see cref="AggregateQuery{T}.Limit"/> are applied here by <see cref="AggregateHelper.ApplyOrderingAndPaging"/>, as
        /// Elasticsearch and Cosmos DB apply them; they used to be ignored (measured: every group, in the server's order).</para>
        /// </remarks>
        public static IReadOnlyList<AggregateResult> ReadResults<T>(AggregateQuery<T> query, List<BsonDocument> bsonResults)
            where T : Data.Models.AbstractModel
        {
            if (bsonResults.Count == 0 && !query.IsGrouped)
            {
                return AggregateHelper.LinqAggregate(Array.Empty<T>(), query);
            }

            var readers = new Dictionary<string, Func<BsonValue, object?>>(StringComparer.Ordinal);
            foreach (var field in query.GroupByFields)
            {
                var serializer = Member<T>(field, "group-by field").Serializer;
                readers[field] = value => Deserialize(value, serializer);
            }
            if (!string.IsNullOrEmpty(query.TimeColumn))
            {
                var serializer = Member<T>(query.TimeColumn, "time column").Serializer;
                readers[AggregateBucketAlias] = value => Deserialize(value, serializer);
            }
            foreach (var aggregate in query.Aggregates)
            {
                readers[aggregate.ResolvedAlias] = ReaderOf<T>(aggregate);
            }

            var rows = new List<AggregateResult>(bsonResults.Count);
            foreach (var doc in bsonResults)
            {
                for (var i = 0; i < query.Aggregates.Count; i++)
                {
                    var aggregate = query.Aggregates[i];
                    if (aggregate.Function is not (AggregateFunction.Min or AggregateFunction.Max)) continue;
                    ThrowIfUnsound(doc, CheckOwner(i), aggregate.Function, Owner<T>(), $"{typeof(T).Name}.{aggregate.SourcePropertyName}");
                }
                var row = new Dictionary<string, object?>();
                foreach (var element in doc.Elements)
                {
                    row[element.Name] = readers.TryGetValue(element.Name, out var read) ? read(element.Value) : MapValue(element.Value);
                }
                rows.Add(new AggregateResult(row));
            }
            return AggregateHelper.ApplyOrderingAndPaging(rows, query.OrderBy, query.Offset, query.Limit).AsReadOnly();
        }

        /// <summary>
        /// How an aggregate's answer is read: a Count as an <c>int</c>; a Sum or Avg as the number
        /// <see cref="AggregateMath.ResultType"/> names; a Min or Max as its member is read.
        /// </summary>
        private static Func<BsonValue, object?> ReaderOf<T>(AggregateField aggregate)
        {
            if (aggregate.Function == AggregateFunction.Count) return value => ToNumberOrNull(value, typeof(int));
            var (_, serializer, type) = Member<T>(aggregate.SourcePropertyName, "aggregate source");
            if (aggregate.Function is AggregateFunction.Min or AggregateFunction.Max)
            {
                var memberName = $"{typeof(T).Name}.{aggregate.SourcePropertyName}";
                if (TextEnumNames(serializer, type, Owner<T>(), aggregate.Function, memberName) != null) return value => EnumOfValue(value, type);
                return value => Deserialize(value, serializer);
            }
            var resultType = AggregateMath.ResultType(aggregate.Function, type);
            return value => ToNumberOrNull(value, resultType);
        }

        private static object? ToNumberOrNull(BsonValue value, Type type)
        {
            var mapped = MapValue(value);
            return mapped == null ? null : ToNumber(mapped, type);
        }

        private static object? MapValue(BsonValue value)
        {
            if (value.IsBsonNull) return null;
            try
            {
                return BsonTypeMapper.MapToDotNetValue(value);
            }
            catch
            {
                return value.ToString();
            }
        }

        /// <summary>A stored value as <paramref name="serializer"/> reads it, the way a read of the member reads it.</summary>
        private static object? Deserialize(BsonValue value, IBsonSerializer serializer)
        {
            if (value.IsBsonNull || value.IsBsonUndefined) return null;
            using var reader = new global::MongoDB.Bson.IO.BsonDocumentReader(new BsonDocument("v", value));
            reader.ReadStartDocument();
            reader.ReadName();
            return serializer.Deserialize(BsonDeserializationContext.CreateRoot(reader));
        }

        /// <summary>A number the driver mapped (an <c>int</c>, <c>long</c>, <c>double</c> or <c>Decimal128</c>) as <paramref name="type"/>, checked.</summary>
        private static object ToNumber(object value, Type type)
        {
            if (value is Decimal128 d128) value = Decimal128.ToDecimal(d128);
            return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
