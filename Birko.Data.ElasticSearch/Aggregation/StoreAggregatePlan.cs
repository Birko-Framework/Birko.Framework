using Birko.Data.Stores;
using Nest;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Birko.Data.ElasticSearch.Aggregation
{
    /// <summary>
    /// An <see cref="AggregateQuery{T}"/> resolved against the document's mapping, and the requests and row reading that
    /// answer it. The one producer behind <see cref="Stores.ElasticSearchStore{T}.Aggregate"/> and its async twin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-878. The names of a query are property names, as on SQL (<c>ResolveSqlName</c>) and in memory
    /// (<see cref="AggregateHelper"/>); a stored field name is accepted too, as for sorts. They used to be sent verbatim, and
    /// Elasticsearch answers an aggregation over a field the index does not have with nothing rather than an error: a
    /// group-by on <c>Bucket</c> (stored <c>bucket</c>) returned no groups, a metric over <c>A</c> read null, a sum 0, a
    /// time bucket on <c>At</c> no buckets; the stored name of a string was a 400, since <c>text</c> cannot be aggregated.
    /// A name is now resolved through <see cref="ElasticSearch.ResolveMember"/> and <see cref="ElasticSearch.StoredFieldName"/>
    /// (a group key through <see cref="ElasticSearch.ExactFieldName"/>, the keyword form), or refused.
    /// </para>
    /// <para>
    /// What is answered matches the LINQ aggregation over the same rows: <c>Count</c> is the number of rows (the bucket's
    /// doc count, never a value count of its source, which read 0); a null key is a group (composite <c>missing_bucket</c>);
    /// a sum of no values is null, not 0; keys and metrics come back as the member's type; every page of groups is read;
    /// and a query over no rows answers no rows. What cannot match is refused by name: a metric over a string or a Guid,
    /// a group on a <c>[Text]</c> or non-scalar member, a time bucket not aligned as LINQ aligns it.
    /// </para>
    /// </remarks>
    internal sealed class StoreAggregatePlan
    {
        private const string GroupAggregation = "group_by";
        private const string TimeSource = "t";

        private readonly List<GroupKey> _groups = new();
        private readonly List<Metric> _metrics = new();
        private readonly TimeBucket? _time;
        private readonly int _pageSize;

        private StoreAggregatePlan(TimeBucket? time, int pageSize)
        {
            _time = time;
            _pageSize = pageSize;
        }

        private bool Grouped => _groups.Count > 0 || _time != null;

        /// <summary>Resolves every name of <paramref name="query"/>, or refuses the query before anything is sent.</summary>
        /// <exception cref="ArgumentException">A name resolves to no property, or the time bucket is incomplete or does not parse.</exception>
        /// <exception cref="NotSupportedException">A member Elasticsearch cannot aggregate the way LINQ does.</exception>
        public static StoreAggregatePlan For<T>(AggregateQuery<T> query, int pageSize) where T : Models.AbstractModel
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));

            var plan = new StoreAggregatePlan(ResolveTime(typeof(T), query.TimeColumn, query.TimeBucketInterval), pageSize);

            for (var i = 0; i < query.GroupByFields.Count; i++)
            {
                var name = query.GroupByFields[i];
                var member = ElasticSearch.ResolveMember(typeof(T), name, "Aggregate group-by field");
                var type = RequireScalar(member, "group by");
                var field = ElasticSearch.ExactFieldName(member, member.PropertyType, null)!;
                // A dynamically mapped string is grouped on its keyword sub-field, which skips a value past ignore_above:
                // such a row would land in the null group. The stored text field still says it has a value.
                var textField = field.EndsWith(".keyword", StringComparison.Ordinal) ? ElasticSearch.StoredFieldName(member, null) : null;
                plan._groups.Add(new GroupKey(name, "k" + Index(i), field, type, textField, "x" + Index(i)));
            }

            for (var i = 0; i < query.Aggregates.Count; i++)
            {
                var aggregate = query.Aggregates[i];
                if (aggregate.Function == AggregateFunction.Count)
                {
                    // As COUNT(*) and LINQ's group.Count(): the rows, whatever the source names.
                    plan._metrics.Add(new Metric(aggregate.ResolvedAlias, AggregateFunction.Count, null, null, null, null));
                    continue;
                }

                var member = ElasticSearch.ResolveMember(typeof(T), aggregate.SourcePropertyName, "Aggregate source");
                var type = RequireMetricType(member, aggregate.Function);
                var field = ElasticSearch.StoredFieldName(member, null)!;
                plan._metrics.Add(new Metric(aggregate.ResolvedAlias, aggregate.Function, type, field, "m" + Index(i), "c" + Index(i)));
            }

            return plan;
        }

        /// <summary>The request for one page: <paramref name="after"/> is the previous page's last key, null for the first.</summary>
        public SearchRequest Request(string indexName, QueryContainer? filter, CompositeKey? after)
        {
            var metrics = MetricAggregations();
            if (!Grouped)
            {
                return new SearchRequest(indexName)
                {
                    Size = 0,
                    Query = filter,
                    TrackTotalHits = true,
                    Aggregations = metrics
                };
            }

            var sources = new List<ICompositeAggregationSource>();
            if (_time != null)
            {
                sources.Add(new DateHistogramCompositeAggregationSource(TimeSource)
                {
                    Field = _time.Field,
                    FixedInterval = _time.Interval,
                    MissingBucket = true
                });
            }
            foreach (var group in _groups)
            {
                sources.Add(new TermsCompositeAggregationSource(group.Source) { Field = group.Field, MissingBucket = true });
                if (group.TextField != null)
                {
                    metrics.Add(group.TextCheck, new AggregationContainer { Filter = new FilterAggregation(group.TextCheck) { Filter = new ExistsQuery { Field = group.TextField } } });
                }
            }

            // ⚠ Added as the aggregation, not as `new AggregationContainer { Composite = … }`: NEST's conversion from an
            // aggregation to its container is what carries the sub-aggregations over. A container built by hand sends the
            // composite without its metrics, and each reads null (measured; the old terms builder did this, so a Count read 0).
            var composite = new CompositeAggregation(GroupAggregation)
            {
                Size = _pageSize,
                Sources = sources,
                Aggregations = metrics,
                After = after
            };
            return new SearchRequest(indexName)
            {
                Size = 0,
                Query = filter,
                Aggregations = new AggregationDictionary { { GroupAggregation, composite } }
            };
        }

        /// <summary>
        /// Adds the rows of <paramref name="response"/> to <paramref name="rows"/>, and returns the key to read the next page
        /// after, or null when this was the last.
        /// </summary>
        public CompositeKey? ReadPage<T>(ISearchResponse<T> response, string indexName, List<AggregateResult> rows) where T : class
        {
            if (!response.IsValid || response.OriginalException != null)
            {
                throw new InvalidOperationException(
                    $"ElasticSearch aggregation query failed. Index: {indexName}. DebugInfo: {response.DebugInformation}",
                    response.OriginalException);
            }

            if (!Grouped)
            {
                // LINQ groups no rows into no groups, so a query over nothing answers nothing.
                if (response.Total == 0) return null;
                var row = new Dictionary<string, object?>();
                ReadMetrics(response.Aggregations, response.Total, row);
                rows.Add(new AggregateResult(row));
                return null;
            }

            var page = response.Aggregations.Composite(GroupAggregation)
                ?? throw new InvalidOperationException($"ElasticSearch returned no {GroupAggregation} aggregation. Index: {indexName}.");
            var buckets = page.Buckets.ToList();
            foreach (var bucket in buckets)
            {
                var row = new Dictionary<string, object?>();
                IReadOnlyDictionary<string, object> key = bucket.Key;
                foreach (var group in _groups)
                {
                    key.TryGetValue(group.Source, out var value);
                    if (value == null && group.TextField != null && (bucket.Filter(group.TextCheck)?.DocCount ?? 0) > 0)
                    {
                        throw new InvalidOperationException(
                            $"Aggregate group-by field '{group.Name}' has values longer than its keyword sub-field indexes (ignore_above), "
                            + "and Elasticsearch would group them with the nulls. Map it with [Keyword] to group by it.");
                    }
                    row[group.Name] = ConvertKey(value, group.Type);
                }
                if (_time != null)
                {
                    key.TryGetValue(TimeSource, out var time);
                    row["bucket_time"] = time == null ? null : FromEpochMilliseconds(Convert.ToDouble(time, CultureInfo.InvariantCulture));
                }
                ReadMetrics(bucket, bucket.DocCount ?? 0, row);
                rows.Add(new AggregateResult(row));
            }

            return buckets.Count < _pageSize || page.AfterKey == null ? null : page.AfterKey;
        }

        private AggregationDictionary MetricAggregations()
        {
            var dict = new AggregationDictionary();
            foreach (var metric in _metrics)
            {
                if (metric.Function == AggregateFunction.Count) continue;
                dict.Add(metric.Name!, StoreAggregationHelper.BuildSingleMetricAggregation(metric.Function, metric.Name!, metric.Field));
                if (metric.Function == AggregateFunction.Sum)
                {
                    // Elasticsearch sums no values to 0; LINQ to null. The value count tells them apart.
                    dict.Add(metric.ValueCount!, new AggregationContainer { ValueCount = new ValueCountAggregation(metric.ValueCount!, metric.Field) });
                }
            }
            return dict;
        }

        private void ReadMetrics(AggregateDictionary source, long rowCount, Dictionary<string, object?> row)
        {
            foreach (var metric in _metrics)
            {
                row[metric.Alias] = metric.Function switch
                {
                    AggregateFunction.Count => (object)checked((int)rowCount),
                    AggregateFunction.Sum => (source.ValueCount(metric.ValueCount!)?.Value ?? 0) == 0 ? null : ConvertMetric(source.Sum(metric.Name!)?.Value, metric),
                    AggregateFunction.Avg => ConvertMetric(source.Average(metric.Name!)?.Value, metric),
                    AggregateFunction.Min => ConvertMetric(source.Min(metric.Name!)?.Value, metric),
                    AggregateFunction.Max => ConvertMetric(source.Max(metric.Name!)?.Value, metric),
                    _ => throw new NotSupportedException($"Aggregate function {metric.Function} is not supported.")
                };
            }
        }

        private static TimeBucket? ResolveTime(Type type, string? column, string? interval)
        {
            if (string.IsNullOrEmpty(column) && string.IsNullOrEmpty(interval)) return null;
            // As AggregateHelper: one without the other, or an interval that does not parse, is refused. The interval used
            // to default to an hour, and a column alone was ignored.
            if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(interval))
                throw new ArgumentException("Time bucketing needs both TimeBucketInterval and TimeColumn.", nameof(column));
            var span = TimeIntervalParser.Parse(interval);
            if (span <= TimeSpan.Zero)
                throw new ArgumentException($"Time bucket interval '{interval}' is not a positive interval.", nameof(interval));

            var member = ElasticSearch.ResolveMember(type, column, "Aggregate time column");
            var memberType = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (memberType != typeof(DateTime))
                throw new NotSupportedException($"Aggregate time column '{column}' is {memberType.Name}; only a DateTime member is bucketed.");

            // LINQ buckets from 0001-01-01, Elasticsearch from 1970-01-01. They agree only for an interval that divides the
            // time between the two (any divisor of a day does; a week does not), in whole milliseconds.
            if (span.Ticks % TimeSpan.TicksPerMillisecond != 0 || DateTime.UnixEpoch.Ticks % span.Ticks != 0)
                throw new NotSupportedException(
                    $"Time bucket interval '{interval}' does not divide the time from 0001-01-01 to 1970-01-01 in whole milliseconds, "
                    + "so Elasticsearch's buckets would start at other times than the in-memory ones. Use an interval that divides a day.");

            return new TimeBucket(ElasticSearch.StoredFieldName(member, null)!, new Nest.Time(span));
        }

        private static Type RequireScalar(PropertyInfo member, string use)
        {
            var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (type == typeof(string) || type == typeof(bool) || type == typeof(Guid) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type.IsEnum || IsNumeric(type))
                return type;
            throw new NotSupportedException(
                $"Cannot {use} {member.DeclaringType?.Name}.{member.Name}: a {type.Name} is not a single value Elasticsearch can aggregate.");
        }

        private static Type RequireMetricType(PropertyInfo member, AggregateFunction function)
        {
            var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            var allowed = function is AggregateFunction.Sum or AggregateFunction.Avg
                ? IsNumeric(type)
                // A string or Guid is ordered by culture or by field in C#, by bytes in a keyword: refused, not reordered.
                : IsNumeric(type) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(bool) || type.IsEnum;
            if (allowed) return type;
            throw new NotSupportedException(
                $"Cannot compute {function} of {member.DeclaringType?.Name}.{member.Name}: Elasticsearch has no {function} of a {type.Name} that matches C#'s.");
        }

        private static bool IsNumeric(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(decimal)
               || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint)
               || type == typeof(ulong);

        private static object? ConvertKey(object? value, Type type)
        {
            if (value == null) return null;
            if (type == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (type == typeof(Guid)) return Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            if (type == typeof(bool)) return value is bool b ? b : Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
            if (type == typeof(DateTime)) return FromEpochMilliseconds(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            if (type == typeof(DateTimeOffset)) return new DateTimeOffset(FromEpochMilliseconds(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            if (type.IsEnum)
                return value is string name ? Enum.Parse(type, name) : Enum.ToObject(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        /// <summary>A metric as the type LINQ's aggregate of <paramref name="metric"/>'s member returns.</summary>
        private static object? ConvertMetric(double? value, Metric metric)
        {
            if (value == null) return null;
            var type = metric.Type!;
            var v = value.Value;
            switch (metric.Function)
            {
                case AggregateFunction.Sum:
                    if (type == typeof(int)) return checked((int)Math.Round(v));
                    if (type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte)
                        || type == typeof(ushort) || type == typeof(uint)) return checked((long)Math.Round(v));
                    if (type == typeof(ulong) || type == typeof(decimal)) return (decimal)v;
                    if (type == typeof(float)) return (float)v;
                    return v;
                case AggregateFunction.Avg:
                    if (type == typeof(decimal)) return (decimal)v;
                    if (type == typeof(float)) return (float)v;
                    return v;
                default:
                    if (type == typeof(bool)) return v != 0;
                    if (type == typeof(DateTime)) return FromEpochMilliseconds(v);
                    if (type == typeof(DateTimeOffset)) return new DateTimeOffset(FromEpochMilliseconds(v));
                    if (type.IsEnum) return Enum.ToObject(type, checked((long)v));
                    if (type == typeof(decimal)) return (decimal)v;
                    if (type == typeof(double)) return v;
                    if (type == typeof(float)) return (float)v;
                    return Convert.ChangeType(Math.Round(v), type, CultureInfo.InvariantCulture);
            }
        }

        private static DateTime FromEpochMilliseconds(double milliseconds)
            => DateTime.UnixEpoch.AddTicks(checked((long)Math.Round(milliseconds) * TimeSpan.TicksPerMillisecond));

        private static string Index(int i) => i.ToString(CultureInfo.InvariantCulture);

        private sealed record GroupKey(string Name, string Source, string Field, Type Type, string? TextField, string TextCheck);

        /// <summary>A metric: <paramref name="Name"/> is its aggregation (null for Count, read from the doc count).</summary>
        private sealed record Metric(string Alias, AggregateFunction Function, Type? Type, string? Field, string? Name, string? ValueCount);

        private sealed record TimeBucket(string Field, Nest.Time Interval);
    }
}
