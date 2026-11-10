using Birko.Data.Stores;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Birko.Data.CosmosDB.Aggregation
{
    /// <summary>
    /// Shared helper for building Cosmos DB SQL aggregation queries and parsing JSON results.
    /// Used by both <see cref="Stores.CosmosDBStore{T}"/> and <see cref="Stores.AsyncCosmosDBStore{T}"/>.
    /// </summary>
    public static class CosmosAggregationHelper
    {
        /// <summary>
        /// Returns the Cosmos DB SQL aggregate expression for the given function and field.
        /// For Count returns "COUNT(1)", for others "FUNC(IS_NULL(field) ? undefined : field)": over the field's non-null
        /// values only. Shared by store-level and view-level aggregation.
        /// </summary>
        /// <remarks>
        /// TASK-885: a null is a value to Cosmos DB's aggregates, not an absence. Measured on the emulator (vnext-preview,
        /// 2026-11-10), over a group holding a null: <c>SUM</c> and <c>AVG</c> answer undefined (SUM(Score) nothing where
        /// C# gives 140), and <c>MIN</c> answers null, which sorts below every number and string (MIN(Score) null where C#
        /// gives 10), on the store aggregate and on a grouped view alike; <c>MAX</c> was right. C# and every other provider
        /// skip nulls. A missing field is undefined, which every aggregate already skips, so the null is turned into
        /// undefined (<c>c.X ?? undefined</c> does not: <c>??</c> only replaces undefined). Measured with the conditional:
        /// SUM 140, MIN 10, AVG 35, MAX 60, also for decimal, double, string and DateTime members. Over no non-null value
        /// SUM answers 0, the others undefined; the store aggregate makes that SUM null (<see cref="CosmosAggregatePlan"/>).
        /// </remarks>
        public static string BuildFunctionSql(AggregateFunction function, string? fieldExpression)
        {
            return function switch
            {
                AggregateFunction.Count => "COUNT(1)",
                AggregateFunction.Sum => $"SUM({NonNull(fieldExpression)})",
                AggregateFunction.Avg => $"AVG({NonNull(fieldExpression)})",
                AggregateFunction.Min => $"MIN({NonNull(fieldExpression)})",
                AggregateFunction.Max => $"MAX({NonNull(fieldExpression)})",
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

        /// <summary>TASK-885: the field's value, or undefined where it is null, so an aggregate skips it as C# does.</summary>
        internal static string NonNull(string? fieldExpression)
            => $"IS_NULL({fieldExpression}) ? undefined : {fieldExpression}";

        /// <summary>TASK-885: how many of the group's values of the field are not null (a missing field is not counted).</summary>
        internal static string NonNullCount(string fieldExpression) => $"COUNT({NonNull(fieldExpression)})";

        /// <summary>
        /// Builds the SELECT and GROUP BY parts of a Cosmos DB SQL aggregation query.
        /// Returns them separately so callers can insert WHERE between them.
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        /// <param name="groupByFields">Tuples of (fieldName, alias) for GROUP BY columns.</param>
        /// <param name="aggregates">Tuples of (function, sourceProperty, alias) for aggregate columns.</param>
        /// <returns>A tuple of (selectFromSql, groupBySql) where groupBySql is null if no GROUP BY fields.</returns>
        public static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias)> aggregates)
            => BuildAggregateSqlParts(groupByFields, aggregates, "c", Array.Empty<(string, string)>(), Array.Empty<string>());

        /// <summary>
        /// The SELECT and GROUP BY parts over the container named <paramref name="source"/>: <c>FROM source</c>, and every
        /// accessor <c>source["Field"]</c>. <paramref name="extraColumns"/> are selected after the aggregates, and
        /// <paramref name="extraGroupBy"/> grouped after the fields (TASK-885: the time bucket and the non-null counts).
        /// </summary>
        private static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias)> aggregates,
            string source,
            IReadOnlyList<(string Expression, string Alias)> extraColumns,
            IReadOnlyList<string> extraGroupBy)
        {
            var selectParts = new List<string>();
            var groupByFieldList = groupByFields.ToList();

            foreach (var (fieldName, alias) in groupByFieldList)
            {
                selectParts.Add($"{FieldRef(fieldName, source)} AS {Alias(alias)}");
            }

            foreach (var (function, sourceProperty, alias) in aggregates)
            {
                var fieldExpr = function == AggregateFunction.Count ? null : FieldRef(sourceProperty, source);
                selectParts.Add($"{BuildFunctionSql(function, fieldExpr)} AS {Alias(alias)}");
            }

            foreach (var (expression, alias) in extraColumns)
            {
                selectParts.Add($"{expression} AS {Alias(alias)}");
            }

            var selectFromSql = $"SELECT {string.Join(", ", selectParts)} FROM {source}";

            var groupByParts = groupByFieldList.Select(g => FieldRef(g.FieldName, source)).Concat(extraGroupBy).ToList();
            string? groupBySql = groupByParts.Count > 0
                ? $" GROUP BY {string.Join(", ", groupByParts)}"
                : null;

            return (selectFromSql, groupBySql);
        }

        /// <summary>
        /// Emits a bracket-quoted Cosmos DB property accessor (<c>c["Field"]</c>) instead of the
        /// dotted form (<c>c.Field</c>). Bracket quoting handles field names that would be invalid or
        /// injectable as a dotted identifier (whitespace, reserved words, embedded quotes), and keeps
        /// SELECT/GROUP BY identifiers consistent (CR-M084). A null/empty field falls back to <c>c</c>.
        /// </summary>
        internal static string FieldRef(string? fieldName) => FieldRef(fieldName, "c");

        private static string FieldRef(string? fieldName, string source)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return source;
            }
            // Cosmos string accessors follow JSON string escaping (backslash), same as string literals.
            var escaped = fieldName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{source}[\"{escaped}\"]";
        }

        /// <summary>
        /// TASK-844: a SELECT alias written as a quoted string, <c>AS "Value"</c>, escaped as <see cref="FieldRef(string?)"/>
        /// escapes a field name. A bare alias is parsed as an identifier, so a property named like a keyword
        /// (<c>Inner</c>, <c>Value</c>) was a 400 syntax error on every query (measured on the emulator, vnext-preview,
        /// 2026-11-07: <c>c["Sub"] AS Inner</c> → SC1001). The quoted form names the result property exactly as written,
        /// any characters included. The one producer of every alias this helper and the view store write.
        /// </summary>
        internal static string Alias(string alias)
            => "\"" + alias.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>
        /// TASK-843: the bracket-quoted accessor of a member path, <c>["Sub", "X"]</c> → <c>c["Sub"]["X"]</c>, each
        /// segment quoted as <see cref="FieldRef(string?)"/> quotes one. A one-segment path is exactly
        /// <see cref="FieldRef(string?)"/>'s output.
        /// </summary>
        internal static string PathRef(IReadOnlyList<string> path)
        {
            var sb = new System.Text.StringBuilder("c");
            foreach (var segment in path)
            {
                // FieldRef(segment) is c["segment"]: the accessor is everything after the leading "c".
                var single = FieldRef(segment);
                sb.Append(single, 1, single.Length - 1);
            }
            return sb.ToString();
        }

        /// <summary>The alias the Cosmos DB LINQ provider gives the container in the text it renders.</summary>
        private const string LinqRoot = "root";

        /// <summary>What the LINQ provider renders for an unfiltered <c>GetItemLinqQueryable&lt;T&gt;()</c>.</summary>
        private const string LinqSelectAll = "SELECT VALUE root FROM root";

        /// <summary>What it renders in front of the predicate of <c>GetItemLinqQueryable&lt;T&gt;().Where(filter)</c>.</summary>
        private const string LinqWherePrefix = LinqSelectAll + " WHERE ";

        /// <summary>
        /// TASK-876: the store-level aggregation query, <c>SELECT … FROM root [WHERE …] [GROUP BY …]</c>, whose WHERE is
        /// the one the store's reads send.
        /// </summary>
        /// <remarks>
        /// <para>The method this replaces, <c>BuildAggregateSql</c>, took no filter and emitted no WHERE, so
        /// <c>Aggregate</c> / <c>AggregateAsync</c> aggregated the whole container whatever <c>query.Filter</c> said.
        /// Measured on the emulator (vnext-preview, 2026-11-09): <c>A &gt; 3</c> counted 6 rows of 6, and every filtered
        /// shape answered the unfiltered aggregate.</para>
        /// <para><b>One producer.</b> The predicate is not translated here. <paramref name="filtered"/> is what the store's
        /// own reads send, <c>GetItemLinqQueryable&lt;T&gt;().Where(filter).ToQueryDefinition()</c> after the same rewrites
        /// (<c>SpanContains</c>, <c>DateTruncation</c>, <c>NullOrderingFold</c>). Its text is
        /// <c>SELECT VALUE root FROM root WHERE &lt;predicate&gt;</c>; the predicate is taken from it with every parameter it
        /// carries, and the aggregate is written over the provider's own alias, <c>root</c>. A rendering of any other
        /// shape is refused by name, never guessed at: a filter is never dropped.</para>
        /// <para>TASK-885: every SUM also selects how many non-null values it saw, so a SUM over none answers null, as the
        /// in-memory aggregation does (Cosmos DB answers 0); a time bucket is grouped on as
        /// <see cref="CosmosAggregatePlan"/> describes. Those columns are read and removed by
        /// <see cref="CosmosAggregatePlan.Read"/>.</para>
        /// </remarks>
        /// <param name="query">The aggregation. Its filter is not read here; <paramref name="filtered"/> carries it.</param>
        /// <param name="filtered">The store's rendering of the rewritten filter, or <c>null</c> for no filter.</param>
        /// <exception cref="NotSupportedException">The rendering is not a plain WHERE over the container, or the time bucket
        /// cannot be answered as the in-memory aggregation answers it.</exception>
        /// <exception cref="ArgumentException">The time bucket is incomplete, does not parse, or names no property.</exception>
        internal static CosmosAggregatePlan BuildAggregateQuery<T>(AggregateQuery<T> query, QueryDefinition? filtered)
            where T : Data.Models.AbstractModel
        {
            var bucket = CosmosAggregatePlan.ResolveTimeBucket<T>(query.TimeColumn, query.TimeBucketInterval);

            var used = new HashSet<string>(query.GroupByFields, StringComparer.Ordinal);
            used.UnionWith(query.Aggregates.Select(a => a.ResolvedAlias));
            used.Add(CosmosAggregatePlan.BucketAlias);

            var extraColumns = new List<(string Expression, string Alias)>();
            var extraGroupBy = new List<string>();
            var sumCounts = new List<(string Alias, string CountAlias)>();
            foreach (var aggregate in query.Aggregates.Where(a => a.Function == AggregateFunction.Sum))
            {
                var countAlias = HiddenAlias(used);
                extraColumns.Add((NonNullCount(FieldRef(aggregate.SourcePropertyName, LinqRoot)), countAlias));
                sumCounts.Add((aggregate.ResolvedAlias, countAlias));
            }

            string? timeCountAlias = null;
            if (bucket != null)
            {
                var column = FieldRef(bucket.Value.Member, LinqRoot);
                var ms = bucket.Value.Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var expression = $"FLOOR(DateTimeToTimestamp(LEFT({column}, 19)) / {ms}) * {ms}";
                extraColumns.Add((expression, CosmosAggregatePlan.BucketAlias));
                extraGroupBy.Add(expression);
                timeCountAlias = HiddenAlias(used);
                extraColumns.Add((NonNullCount(column), timeCountAlias));
            }

            var (selectFromSql, groupBySql) = BuildAggregateSqlParts(
                query.GroupByFields.Select(f => (f, f)),
                query.Aggregates.Select(a => (a.Function, (string?)a.SourcePropertyName, a.ResolvedAlias)),
                LinqRoot,
                extraColumns,
                extraGroupBy);

            var where = filtered == null ? null : WhereOf<T>(filtered.QueryText);
            var definition = new QueryDefinition(selectFromSql + (where == null ? "" : " WHERE " + where) + (groupBySql ?? ""));
            if (filtered != null)
            {
                foreach (var (name, value) in filtered.GetQueryParameters())
                {
                    definition = definition.WithParameter(name, value);
                }
            }

            return new CosmosAggregatePlan(
                definition,
                typeof(T).Name,
                query.GroupByFields,
                query.Aggregates.Select(a => a.ResolvedAlias).ToList(),
                sumCounts,
                bucket == null ? null : (bucket.Value.Member, timeCountAlias!));
        }

        /// <summary>A result column name no group field or aggregate alias uses.</summary>
        private static string HiddenAlias(HashSet<string> used)
        {
            var alias = "__birko_n" + used.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (!used.Add(alias)) alias += "_";
            return alias;
        }

        /// <summary>The predicate of a rendered <c>Where</c>, or <c>null</c> when the provider rendered none.</summary>
        private static string? WhereOf<T>(string linqText)
        {
            if (string.Equals(linqText, LinqSelectAll, StringComparison.Ordinal)) return null;
            if (linqText.Length > LinqWherePrefix.Length && linqText.StartsWith(LinqWherePrefix, StringComparison.Ordinal))
            {
                return linqText.Substring(LinqWherePrefix.Length);
            }
            throw new NotSupportedException(
                $"Cosmos DB aggregate of {typeof(T).Name} refused: its filter did not render as a plain WHERE over the "
                + "container, so it cannot be placed in front of the aggregation, and a filter is never dropped. "
                + "Aggregate the rows of a read instead. (TASK-876)");
        }

        /// <summary>
        /// Parses a <see cref="JsonElement"/> document into a dictionary of .NET values.
        /// Handles Number, String, Boolean, Null, and falls back to ToString for other types.
        /// </summary>
        /// <param name="doc">The JSON element from a Cosmos DB query result.</param>
        /// <returns>A dictionary mapping property names to their .NET values.</returns>
        public static Dictionary<string, object?> ParseJsonResult(JsonElement doc)
        {
            var row = new Dictionary<string, object?>();
            foreach (var prop in doc.EnumerateObject())
            {
                row[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.ToString()
                };
            }
            return row;
        }
    }
}
