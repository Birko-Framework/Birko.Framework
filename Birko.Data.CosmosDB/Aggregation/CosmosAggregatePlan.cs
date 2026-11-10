using Birko.Data.Stores;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Birko.Data.CosmosDB.Aggregation
{
    /// <summary>
    /// TASK-885: a store aggregate's query, and how one row of its answer becomes an <see cref="AggregateResult"/> shaped as
    /// <see cref="AggregateHelper.LinqAggregate{T}"/> shapes it. Built by <see cref="CosmosAggregationHelper.BuildAggregateQuery{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>SUM over no non-null value is 0</b> (TASK-886, the user's decision), as Cosmos DB and the in-memory
    /// aggregation answer it; TASK-885 had made it null with a hidden count of the values. An aggregate Cosmos DB leaves
    /// undefined (AVG, MIN, MAX over no value) is null, and a group key it leaves undefined (a missing field) is null, so
    /// every row carries every key, as in memory.</para>
    /// <para><b>The time bucket</b> (<see cref="AggregateQuery{T}.TimeBucketInterval"/> on <see cref="AggregateQuery{T}.TimeColumn"/>)
    /// used to be ignored, silently: the query was not bucketed at all (measured: one row of 7 where C# has 5 buckets).
    /// It is now grouped on <c>FLOOR(DateTimeToTimestamp(LEFT(t, 19)) / ms) * ms</c>, under <c>bucket_time</c>. Measured on the
    /// emulator (vnext-preview, 2026-11-10):</para>
    /// <list type="bullet">
    /// <item>In memory a bucket is the stored wall-clock time floored from 0001-01-01 (<c>dt.Ticks</c>, whatever its kind).
    /// Cosmos DB's date functions convert an offset to UTC (<c>10:30+01:00</c> bins to <c>09:00Z</c>), so the first 19
    /// characters, the wall-clock time without a fraction or an offset, are what is bucketed. Without the fraction an
    /// interval must be whole seconds.</item>
    /// <item>The bucket counts from 1970-01-01, so it is the in-memory bucket only for an interval that divides the time
    /// from 0001-01-01 to 1970-01-01 (any divisor of a day; not a week), the rule TASK-878 measured for Elasticsearch.
    /// <c>DateTimeBin</c> has the same origin and refuses another (<c>'0001-01-01'</c> answered undefined).</item>
    /// <item>Before 1601-01-01 every date function answers undefined (<c>DateTime.MinValue</c> included), so such a row has
    /// no bucket. That is told apart from a null time by a count of the group's non-null times; a group holding one is
    /// refused by name rather than answered as the null bucket.</item>
    /// <item>The bucket is returned with <see cref="DateTimeKind.Unspecified"/>: it is a wall-clock time, and the
    /// documents' kinds are not known. <see cref="DateTime"/> equality ignores the kind.</item>
    /// </list>
    /// </remarks>
    internal sealed class CosmosAggregatePlan
    {
        /// <summary>The result key of the time bucket, as in memory.</summary>
        internal const string BucketAlias = "bucket_time";

        private readonly string _typeName;
        private readonly IReadOnlyList<(string Name, Type Type)> _groups;
        private readonly IReadOnlyList<Metric> _metrics;
        private readonly (string Column, string CountAlias)? _bucket;

        /// <summary>
        /// TASK-897: one aggregate of the query, and how its answer is read: <paramref name="ResultType"/> is the type the
        /// in-memory aggregation answers in (<see cref="AggregateMath.ResultType"/>). <paramref name="CountAlias"/> is the
        /// hidden count of the values a decimal AVG saw; <paramref name="SumAlias"/> the hidden SUM a decimal AVG
        /// is divided from, in which case <paramref name="Alias"/> itself is not selected.
        /// </summary>
        internal sealed record Metric(string Alias, AggregateFunction Function, Type ResultType, string? CountAlias, string? SumAlias);

        internal CosmosAggregatePlan(
            QueryDefinition query,
            string typeName,
            IReadOnlyList<(string Name, Type Type)> groups,
            IReadOnlyList<Metric> metrics,
            (string Column, string CountAlias)? bucket)
        {
            Query = query;
            _typeName = typeName;
            _groups = groups;
            _metrics = metrics;
            _bucket = bucket;
        }

        /// <summary>The query to send.</summary>
        internal QueryDefinition Query { get; }

        /// <summary>
        /// The serializer options a stored document is read with (<see cref="Serialization.CosmosGuidIdSerializer"/>'s
        /// default), so a group key or a MIN/MAX reads back as the same member of a read does.
        /// </summary>
        private static readonly JsonSerializerOptions ReadOptions = new();

        /// <summary>One answered row, in the in-memory aggregation's names and types; the helper columns are not in it.</summary>
        /// <remarks>
        /// TASK-897: Cosmos DB answers JSON numbers and text, which used to be returned as they parsed: a <c>long</c> or a
        /// <c>double</c> for every number, a Guid or a DateTime as its text. A group key and a MIN/MAX are now read as their
        /// member is read (the same JSON, the same options); a COUNT is an <c>int</c>; a SUM or AVG, which Cosmos DB computes
        /// in double, is converted to <see cref="Metric.ResultType"/>, checked, so an <c>int</c> SUM past
        /// <see cref="int.MaxValue"/> throws <see cref="OverflowException"/> as LINQ's does. A decimal SUM is the double to 15
        /// significant digits (<c>0.1 + 0.2</c> is <c>0.3</c>); a decimal AVG is that SUM divided by the count in decimal.
        /// </remarks>
        /// <exception cref="NotSupportedException">A bucket held a time Cosmos DB cannot bucket (before 1601-01-01).</exception>
        internal AggregateResult Read(JsonElement doc)
        {
            var row = new Dictionary<string, object?>();

            foreach (var (name, type) in _groups)
            {
                row[name] = Member(doc, name, type);
            }

            foreach (var metric in _metrics)
            {
                row[metric.Alias] = Value(doc, metric);
            }

            if (_bucket is { } bucket)
            {
                var nonNullTimes = CountOf(doc, bucket.CountAlias);
                var milliseconds = Property(doc, BucketAlias);
                if (milliseconds == null && nonNullTimes > 0)
                {
                    throw new NotSupportedException(
                        $"Cosmos DB aggregate of {_typeName} refused: {nonNullTimes} value(s) of time column '{bucket.Column}' "
                        + "could not be bucketed. Cosmos DB's date functions answer nothing for a time before 1601-01-01 "
                        + "(DateTime.MinValue included) or for text that is not an ISO 8601 date, so those rows would be "
                        + "answered as the null bucket. Filter them out, or aggregate the rows of a read. (TASK-885)");
                }

                row[BucketAlias] = milliseconds == null ? null : FromEpochMilliseconds(milliseconds.Value.GetDouble());
            }

            return new AggregateResult(row);
        }

        private static object? Value(JsonElement doc, Metric metric)
        {
            switch (metric.Function)
            {
                case AggregateFunction.Count:
                    return checked((int)CountOf(doc, metric.Alias));
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    return Member(doc, metric.Alias, metric.ResultType);
                case AggregateFunction.Avg when metric.SumAlias != null:
                    var count = CountOf(doc, metric.CountAlias!);
                    var sum = Property(doc, metric.SumAlias);
                    return count == 0 || sum == null ? null : (decimal)Number(sum.Value, typeof(decimal)) / count;
                case AggregateFunction.Sum:
                    // TASK-886 (the user's decision): a SUM over no value is 0 in the in-memory aggregation too. TASK-885
                    // had made it null with a hidden count of the values; Cosmos DB answers 0.
                    var total = Property(doc, metric.Alias);
                    return total == null ? AggregateMath.SumOfNoValues(metric.ResultType) : Number(total.Value, metric.ResultType);
                default:
                    var value = Property(doc, metric.Alias);
                    return value == null ? null : Number(value.Value, metric.ResultType);
            }
        }

        /// <summary>A number Cosmos DB computed (in double) as <paramref name="type"/>, checked.</summary>
        private static object Number(JsonElement value, Type type)
        {
            if (type == typeof(int)) return value.TryGetInt64(out var i) ? checked((int)i) : checked((int)value.GetDouble());
            if (type == typeof(long)) return value.TryGetInt64(out var l) ? l : checked((long)value.GetDouble());
            if (type == typeof(decimal)) return value.TryGetInt64(out var m) ? (decimal)m : (decimal)value.GetDouble();
            if (type == typeof(float)) return (float)value.GetDouble();
            if (type == typeof(double)) return value.GetDouble();
            throw new NotSupportedException($"An aggregate of type {type.Name} is not read from a number.");
        }

        /// <summary>A column read as a member of <paramref name="type"/> is read; null where the column is null or absent.</summary>
        private static object? Member(JsonElement doc, string alias, Type type)
        {
            var value = Property(doc, alias);
            return value == null ? null : value.Value.Deserialize(type, ReadOptions);
        }

        /// <summary>The column, or null where it is null or absent (undefined: a missing field, an aggregate of nothing).</summary>
        private static JsonElement? Property(JsonElement doc, string alias)
            => doc.TryGetProperty(alias, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

        private static long CountOf(JsonElement doc, string alias)
        {
            var value = Property(doc, alias);
            if (value == null) return 0;
            return value.Value.TryGetInt64(out var count) ? count : checked((long)value.Value.GetDouble());
        }

        private static DateTime FromEpochMilliseconds(double ms)
            => new DateTime(DateTime.UnixEpoch.Ticks + checked((long)Math.Round(ms) * TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified);

        /// <summary>
        /// The time bucket of a query, refused where Cosmos DB cannot answer it as the in-memory aggregation does: the
        /// checks <see cref="AggregateHelper"/> makes, the same exception types, then the ones described on the class.
        /// </summary>
        /// <returns><c>null</c> when the query has no time bucket; otherwise the column's stored name and the interval.</returns>
        internal static (string Member, long Milliseconds)? ResolveTimeBucket<T>(string? column, string? interval)
        {
            if (string.IsNullOrEmpty(column) && string.IsNullOrEmpty(interval)) return null;
            if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(interval))
                throw new ArgumentException("Time bucketing needs both TimeBucketInterval and TimeColumn.", nameof(column));

            var span = TimeIntervalParser.Parse(interval);
            if (span <= TimeSpan.Zero)
                throw new ArgumentException($"Time bucket interval '{interval}' is not a positive interval.", nameof(interval));

            // The in-memory lookup (TASK-897: the one every aggregate name takes). The query names it by its own name.
            var member = CosmosAggregationHelper.ResolveMember(typeof(T), column, "time column");
            var memberType = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (memberType != typeof(DateTime))
                throw new NotSupportedException(
                    $"Cosmos DB aggregate of {typeof(T).Name} refused: time column '{member.Name}' is {memberType.Name}; only a "
                    + "DateTime member is bucketed. (TASK-885)");

            if (span.Ticks % TimeSpan.TicksPerSecond != 0)
                throw new NotSupportedException(
                    $"Cosmos DB aggregate of {typeof(T).Name} refused: time bucket interval '{interval}' is not a whole number "
                    + "of seconds. The stored time is bucketed without its fraction of a second. (TASK-885)");

            if (DateTime.UnixEpoch.Ticks % span.Ticks != 0)
                throw new NotSupportedException(
                    $"Cosmos DB aggregate of {typeof(T).Name} refused: time bucket interval '{interval}' does not divide the "
                    + "time from 0001-01-01 to 1970-01-01, so Cosmos DB's buckets would start at other times than the "
                    + "in-memory ones. Use an interval that divides a day. (TASK-885)");

            return (member.Name, span.Ticks / TimeSpan.TicksPerMillisecond);
        }
    }
}
