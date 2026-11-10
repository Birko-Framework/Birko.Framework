using Birko.Data.Stores;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace Birko.Data.CosmosDB.Aggregation
{
    /// <summary>
    /// TASK-885: a store aggregate's query, and how one row of its answer becomes an <see cref="AggregateResult"/> shaped as
    /// <see cref="AggregateHelper.LinqAggregate{T}"/> shapes it. Built by <see cref="CosmosAggregationHelper.BuildAggregateQuery{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>SUM over no non-null value is null.</b> Cosmos DB answers 0 (measured), the in-memory aggregation null. Each
    /// SUM is selected with a count of the values it saw; where that is 0 the SUM is null. An aggregate Cosmos DB leaves
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
        private readonly IReadOnlyList<string> _groupFields;
        private readonly IReadOnlyList<string> _aggregateAliases;
        private readonly IReadOnlyList<(string Alias, string CountAlias)> _sumCounts;
        private readonly (string Column, string CountAlias)? _bucket;

        internal CosmosAggregatePlan(
            QueryDefinition query,
            string typeName,
            IReadOnlyList<string> groupFields,
            IReadOnlyList<string> aggregateAliases,
            IReadOnlyList<(string Alias, string CountAlias)> sumCounts,
            (string Column, string CountAlias)? bucket)
        {
            Query = query;
            _typeName = typeName;
            _groupFields = groupFields;
            _aggregateAliases = aggregateAliases;
            _sumCounts = sumCounts;
            _bucket = bucket;
        }

        /// <summary>The query to send.</summary>
        internal QueryDefinition Query { get; }

        /// <summary>One answered row, its helper columns read and removed.</summary>
        /// <exception cref="NotSupportedException">A bucket held a time Cosmos DB cannot bucket (before 1601-01-01).</exception>
        internal AggregateResult Read(JsonElement doc)
        {
            var row = CosmosAggregationHelper.ParseJsonResult(doc);

            foreach (var field in _groupFields)
            {
                if (!row.ContainsKey(field)) row[field] = null;
            }

            foreach (var alias in _aggregateAliases)
            {
                if (!row.ContainsKey(alias)) row[alias] = null;
            }

            foreach (var (alias, countAlias) in _sumCounts)
            {
                if (CountOf(row, countAlias) == 0) row[alias] = null;
                row.Remove(countAlias);
            }

            if (_bucket is { } bucket)
            {
                var nonNullTimes = CountOf(row, bucket.CountAlias);
                row.Remove(bucket.CountAlias);
                row.TryGetValue(BucketAlias, out var milliseconds);
                if (milliseconds == null && nonNullTimes > 0)
                {
                    throw new NotSupportedException(
                        $"Cosmos DB aggregate of {_typeName} refused: {nonNullTimes} value(s) of time column '{bucket.Column}' "
                        + "could not be bucketed. Cosmos DB's date functions answer nothing for a time before 1601-01-01 "
                        + "(DateTime.MinValue included) or for text that is not an ISO 8601 date, so those rows would be "
                        + "answered as the null bucket. Filter them out, or aggregate the rows of a read. (TASK-885)");
                }

                row[BucketAlias] = milliseconds == null ? null : FromEpochMilliseconds(milliseconds);
            }

            return new AggregateResult(row);
        }

        private static long CountOf(Dictionary<string, object?> row, string alias)
            => row.TryGetValue(alias, out var value) && value != null
                ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
                : 0;

        private static DateTime FromEpochMilliseconds(object milliseconds)
        {
            var ms = Convert.ToDouble(milliseconds, System.Globalization.CultureInfo.InvariantCulture);
            return new DateTime(DateTime.UnixEpoch.Ticks + checked((long)Math.Round(ms) * TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified);
        }

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

            // The in-memory lookup: a public instance property, any case. The query names it as it is stored, by its own name.
            var member = typeof(T).GetProperty(column, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? throw new ArgumentException($"Aggregate time column '{column}' is not a property of {typeof(T).Name}.", nameof(column));
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
