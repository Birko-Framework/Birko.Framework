using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Stores;

namespace Birko.Data.InfluxDB.Stores
{
    /// <summary>
    /// The one producer of the sync and async InfluxDB stores' bulk read (TASK-720): which part of a read the server runs
    /// and which part runs here. The filter is a C# predicate run on the returned rows, so whenever there is one the page
    /// (offset, limit) and the sort are taken here, after it; they used to go to the server first, so a filtered page held
    /// fewer rows, or other ones, than asked (measured). A sort whose fields do not share one direction also runs here,
    /// as Flux's <c>sort</c> takes one direction for all its columns (every field took the first's, measured).
    /// </summary>
    internal static class InfluxReadPlan
    {
        /// <summary>Whether the sort and the page run here rather than on the server.</summary>
        public static bool InMemory<T>(Expression<Func<T, bool>>? filter, OrderBy<T>? orderBy)
            => filter != null || (orderBy?.Fields.Count > 1 && orderBy.Fields.Select(f => f.Descending).Distinct().Count() > 1);

        /// <summary>The Flux of a bulk read of <paramref name="measurement"/>: every entity's latest point, then the server's share.</summary>
        public static string Flux<T>(string bucket, string measurement, Expression<Func<T, bool>>? filter, OrderBy<T>? orderBy, int? limit, int? offset)
        {
            var flux = $"from(bucket: \"{bucket}\") " +
                       $"|> range(start: 0) " +
                       $"|> filter(fn: (r) => r._measurement == \"{measurement}\") " +
                       $"|> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\") " +
                       $"|> group() " +
                       $"|> sort(columns: [\"_time\"], desc: true) " +
                       $"|> unique(column: \"Guid\")";

            if (InMemory(filter, orderBy))
            {
                return flux;
            }

            if (orderBy?.Fields.Count > 0)
            {
                var columns = string.Join(", ", orderBy.Fields.Select(f => $"\"{f.PropertyName}\""));
                var desc = orderBy.Fields[0].Descending ? "true" : "false";
                flux += $" |> sort(columns: [{columns}], desc: {desc})";
            }

            if (offset.HasValue)
            {
                flux += $" |> limit(n: {int.MaxValue}, offset: {offset.Value})";
            }

            if (limit.HasValue)
            {
                flux += $" |> limit(n: {limit.Value})";
            }
            return flux;
        }

        /// <summary>The rows the server returned, filtered, sorted and paged here when <see cref="InMemory"/> says so.</summary>
        public static IEnumerable<T> Finish<T>(List<T> rows, Expression<Func<T, bool>>? filter, OrderBy<T>? orderBy, int? limit, int? offset)
        {
            if (!InMemory(filter, orderBy))
            {
                return rows;
            }

            IEnumerable<T> result = filter != null ? rows.Where(filter.Compile()) : rows;
            result = OrderByHelper.ApplyTo(result, orderBy);
            if (offset.HasValue) result = result.Skip(offset.Value);
            if (limit.HasValue) result = result.Take(limit.Value);
            return result.ToList();
        }

        /// <summary>
        /// A <c>DateTime</c> field as it was written (<c>ToString("o")</c>): a UTC value comes back UTC, a local one local, an
        /// unspecified one unspecified. <c>DateTime.Parse</c> converted a UTC value to local time (measured).
        /// </summary>
        public static DateTime ParseDateTime(string value)
            => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
