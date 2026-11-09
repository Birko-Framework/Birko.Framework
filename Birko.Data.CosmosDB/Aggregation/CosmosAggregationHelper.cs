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
        /// For Count returns "COUNT(1)", for others returns "FUNC(fieldExpression)".
        /// Shared by store-level and view-level aggregation.
        /// </summary>
        public static string BuildFunctionSql(AggregateFunction function, string? fieldExpression)
        {
            return function switch
            {
                AggregateFunction.Count => "COUNT(1)",
                AggregateFunction.Sum => $"SUM({fieldExpression})",
                AggregateFunction.Avg => $"AVG({fieldExpression})",
                AggregateFunction.Min => $"MIN({fieldExpression})",
                AggregateFunction.Max => $"MAX({fieldExpression})",
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

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
            => BuildAggregateSqlParts(groupByFields, aggregates, "c");

        /// <summary>
        /// The SELECT and GROUP BY parts over the container named <paramref name="source"/>: <c>FROM source</c>, and every
        /// accessor <c>source["Field"]</c>.
        /// </summary>
        private static (string SelectFromSql, string? GroupBySql) BuildAggregateSqlParts(
            IEnumerable<(string FieldName, string Alias)> groupByFields,
            IEnumerable<(AggregateFunction Function, string? SourceProperty, string Alias)> aggregates,
            string source)
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

            var selectFromSql = $"SELECT {string.Join(", ", selectParts)} FROM {source}";

            string? groupBySql = groupByFieldList.Count > 0
                ? $" GROUP BY {string.Join(", ", groupByFieldList.Select(g => FieldRef(g.FieldName, source)))}"
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
        /// </remarks>
        /// <param name="query">The aggregation. Its filter is not read here; <paramref name="filtered"/> carries it.</param>
        /// <param name="filtered">The store's rendering of the rewritten filter, or <c>null</c> for no filter.</param>
        /// <exception cref="NotSupportedException">The rendering is not a plain WHERE over the container.</exception>
        internal static QueryDefinition BuildAggregateQuery<T>(AggregateQuery<T> query, QueryDefinition? filtered)
            where T : Data.Models.AbstractModel
        {
            var (selectFromSql, groupBySql) = BuildAggregateSqlParts(
                query.GroupByFields.Select(f => (f, f)),
                query.Aggregates.Select(a => (a.Function, (string?)a.SourcePropertyName, a.ResolvedAlias)),
                LinqRoot);

            var where = filtered == null ? null : WhereOf<T>(filtered.QueryText);
            var definition = new QueryDefinition(selectFromSql + (where == null ? "" : " WHERE " + where) + (groupBySql ?? ""));
            if (filtered == null) return definition;

            foreach (var (name, value) in filtered.GetQueryParameters())
            {
                definition = definition.WithParameter(name, value);
            }
            return definition;
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
